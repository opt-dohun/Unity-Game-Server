#define _POSIX_C_SOURCE 200809L
#include <arpa/inet.h>
#include <errno.h>
#include <locale.h>
#include <netinet/in.h>
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>
#include <sys/socket.h>
#include <time.h>
#include <unistd.h>

#define PORT 8001
#define MAX_SCORES 5000
#define MAX_REQUEST 8192
#define MAX_RESPONSE 16384

typedef struct { char name[64]; int turns, hp; long long created_at; } Score;
static const char *score_file = "server/scores.tsv";

static int utf8_count(const char *text) {
    int count = 0;
    const unsigned char *p = (const unsigned char *)text;
    while (*p) {
        unsigned char c = *p++;
        if (c < 32 || c == 127) return -1;
        if (c < 128) { count++; continue; }
        int extra = (c >= 194 && c <= 223) ? 1 : (c >= 224 && c <= 239) ? 2 : (c >= 240 && c <= 244) ? 3 : -1;
        if (extra < 0) return -1;
        for (int i = 0; i < extra; i++) {
            if (!*p || (*p++ & 0xc0) != 0x80) return -1;
        }
        count++;
    }
    return count;
}

static int decode(char *destination, size_t capacity, const char *source) {
    size_t length = 0;
    for (; *source; source++) {
        unsigned char value = (unsigned char)*source;
        if (value == '+') value = ' ';
        else if (value == '%') {
            if (!source[1] || !source[2]) return 0;
            char hex[3] = { source[1], source[2], 0 };
            char *end;
            long parsed = strtol(hex, &end, 16);
            if (*end) return 0;
            value = (unsigned char)parsed;
            source += 2;
        }
        if (length + 1 >= capacity || value == 0 || value == '\t' || value == '\r' || value == '\n') return 0;
        destination[length++] = (char)value;
    }
    destination[length] = 0;
    return 1;
}

static int parse_score(char *body, Score *score) {
    int seen_name = 0, seen_turns = 0, seen_hp = 0;
    for (char *field = strtok(body, "&"); field; field = strtok(NULL, "&")) {
        char *equals = strchr(field, '=');
        if (!equals) return 0;
        *equals++ = 0;
        if (!strcmp(field, "name")) seen_name = decode(score->name, sizeof score->name, equals);
        else if (!strcmp(field, "turns")) { char *end; long n = strtol(equals, &end, 10); seen_turns = !*end && n >= 1 && n <= 99999; score->turns = (int)n; }
        else if (!strcmp(field, "remainingHp")) { char *end; long n = strtol(equals, &end, 10); seen_hp = !*end && n >= 1 && n <= 100; score->hp = (int)n; }
    }
    int chars = utf8_count(score->name);
    if (!seen_name || !seen_turns || !seen_hp || chars < 1 || chars > 12) return 0;
    score->created_at = (long long)time(NULL);
    return 1;
}

static int score_compare(const void *left, const void *right) {
    const Score *a = left, *b = right;
    if (a->turns != b->turns) return a->turns < b->turns ? -1 : 1;
    if (a->hp != b->hp) return a->hp > b->hp ? -1 : 1;
    return a->created_at < b->created_at ? -1 : a->created_at > b->created_at ? 1 : 0;
}

static size_t load_scores(Score *scores) {
    FILE *file = fopen(score_file, "r");
    if (!file) return 0;
    size_t count = 0;
    char line[256];
    while (count < MAX_SCORES && fgets(line, sizeof line, file)) {
        Score score = {0};
        if (sscanf(line, "%63[^\t]\t%d\t%d\t%lld", score.name, &score.turns, &score.hp, &score.created_at) == 4)
            scores[count++] = score;
    }
    fclose(file);
    qsort(scores, count, sizeof *scores, score_compare);
    return count;
}

static size_t append_json_string(char *output, size_t offset, size_t capacity, const char *value) {
    if (offset + 2 >= capacity) return capacity;
    output[offset++] = '"';
    for (const unsigned char *p = (const unsigned char *)value; *p && offset + 7 < capacity; p++) {
        if (*p == '"' || *p == '\\') { output[offset++] = '\\'; output[offset++] = (char)*p; }
        else if (*p < 32) offset += (size_t)snprintf(output + offset, capacity - offset, "\\u%04x", *p);
        else output[offset++] = (char)*p;
    }
    output[offset++] = '"'; output[offset] = 0;
    return offset;
}

static void send_response(int fd, int status, const char *body) {
    const char *reason = status == 200 ? "OK" : status == 201 ? "Created" : status == 204 ? "No Content" : status == 400 ? "Bad Request" : status == 404 ? "Not Found" : "Server Error";
    char header[512];
    int length = snprintf(header, sizeof header,
        "HTTP/1.1 %d %s\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: %zu\r\n"
        "Access-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: GET, POST, OPTIONS\r\n"
        "Access-Control-Allow-Headers: Content-Type\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n",
        status, reason, strlen(body));
    send(fd, header, (size_t)length, 0);
    send(fd, body, strlen(body), 0);
}

static void handle(int fd) {
    char request[MAX_REQUEST + 1] = {0};
    size_t used = 0;
    char *body = NULL;
    while (used < MAX_REQUEST) {
        ssize_t n = recv(fd, request + used, MAX_REQUEST - used, 0);
        if (n <= 0) return;
        used += (size_t)n; request[used] = 0;
        body = strstr(request, "\r\n\r\n");
        if (body) break;
    }
    if (!body) { send_response(fd, 400, "{\"error\":\"request too large\"}"); return; }
    body += 4;
    char method[8] = {0}, path[128] = {0};
    if (sscanf(request, "%7s %127s", method, path) != 2 || strcmp(path, "/api/scores")) {
        send_response(fd, 404, "{\"error\":\"not found\"}"); return;
    }
    if (!strcmp(method, "OPTIONS")) { send_response(fd, 204, ""); return; }
    if (!strcmp(method, "GET")) {
        Score *scores = calloc(MAX_SCORES, sizeof *scores);
        if (!scores) { send_response(fd, 500, "{\"error\":\"out of memory\"}"); return; }
        size_t count = load_scores(scores);
        char result[MAX_RESPONSE]; size_t offset = 0;
        offset += (size_t)snprintf(result + offset, sizeof result - offset, "{\"scores\":[");
        for (size_t i = 0; i < count && i < 20; i++) {
            if (offset + 256 >= sizeof result) break;
            offset += (size_t)snprintf(result + offset, sizeof result - offset, "%s{\"name\":", i ? "," : "");
            offset = append_json_string(result, offset, sizeof result, scores[i].name);
            offset += (size_t)snprintf(result + offset, sizeof result - offset,
                ",\"turns\":%d,\"remainingHp\":%d,\"createdAt\":%lld}",
                scores[i].turns, scores[i].hp, scores[i].created_at);
        }
        snprintf(result + offset, sizeof result - offset, "]}");
        free(scores);
        send_response(fd, 200, result); return;
    }
    if (strcmp(method, "POST")) { send_response(fd, 400, "{\"error\":\"unsupported method\"}"); return; }
    size_t content_length = 0;
    for (char *line = strstr(request, "\r\n"); line && line < body - 4; ) {
        line += 2;
        if (!strncasecmp(line, "Content-Length:", 15)) content_length = (size_t)strtoul(line + 15, NULL, 10);
        line = strstr(line, "\r\n");
    }
    if (!content_length || content_length > 2048 || (size_t)(body - request) + content_length > MAX_REQUEST) {
        send_response(fd, 400, "{\"error\":\"invalid body size\"}"); return;
    }
    while (used - (size_t)(body - request) < content_length) {
        ssize_t n = recv(fd, request + used, MAX_REQUEST - used, 0);
        if (n <= 0) return;
        used += (size_t)n;
    }
    body[content_length] = 0;
    Score score = {0};
    if (!parse_score(body, &score)) { send_response(fd, 400, "{\"error\":\"invalid score\"}"); return; }
    FILE *file = fopen(score_file, "a");
    if (!file) { send_response(fd, 500, "{\"error\":\"storage unavailable\"}"); return; }
    fprintf(file, "%s\t%d\t%d\t%lld\n", score.name, score.turns, score.hp, score.created_at);
    fclose(file);
    send_response(fd, 201, "{\"ok\":true}");
}

int main(int argc, char **argv) {
    if (argc > 1) score_file = argv[1];
    setlocale(LC_CTYPE, "");
    signal(SIGPIPE, SIG_IGN);
    int server = socket(AF_INET, SOCK_STREAM, 0);
    if (server < 0) { perror("socket"); return 1; }
    int yes = 1; setsockopt(server, SOL_SOCKET, SO_REUSEADDR, &yes, sizeof yes);
    struct sockaddr_in address = {0};
    address.sin_family = AF_INET; address.sin_port = htons(PORT);
    inet_pton(AF_INET, "127.0.0.1", &address.sin_addr);
    if (bind(server, (struct sockaddr *)&address, sizeof address) || listen(server, 16)) {
        perror("bind/listen"); close(server); return 1;
    }
    printf("Crimson Tide leaderboard: http://127.0.0.1:%d/api/scores\n", PORT); fflush(stdout);
    for (;;) {
        int client = accept(server, NULL, NULL);
        if (client < 0) { if (errno == EINTR) continue; perror("accept"); break; }
        struct timeval timeout = { .tv_sec = 3, .tv_usec = 0 };
        setsockopt(client, SOL_SOCKET, SO_RCVTIMEO, &timeout, sizeof timeout);
        handle(client); close(client);
    }
    close(server); return 1;
}
