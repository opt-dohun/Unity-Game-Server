using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Random = UnityEngine.Random;

/// <summary>Turn-based boss encounter with explicit player choices and animated action phases.</summary>
public sealed class TurnBasedBossBattle : MonoBehaviour
{
    private enum Phase { Menu, Choice, HeroAction, BossTell, BossAction, BossRecover, Finished }
    private enum Action { None, Attack, Guard }
    private enum ScoreScreen { None, Register, Ranking }
    [Serializable] private sealed class ScoreEntry { public string name; public int turns; public int remainingHp; }
    [Serializable] private sealed class ScoreResponse { public ScoreEntry[] scores; }
    [Serializable] private sealed class BattleStateResponse { public string battleId; public int turn, heroHp, bossHp, bossStage, pattern; public string status; }
    [Serializable] private sealed class TurnRequest { public int turn; public string action; }
    [Serializable] private sealed class TurnResponse { public string battleId, action, status; public int turn, attackDamage, bossDamage, heroHp, bossHp, bossStage, pattern, nextTurn; }
    [Serializable] private sealed class RegisterRequest { public string name; }
    private const string Api = "http://127.0.0.1:8001/api";
    private const float Floor = -2.55f;
    private readonly Sprite[] idle = new Sprite[4], attack = new Sprite[4], guard = new Sprite[4], bossArt = new Sprite[8];
    private SpriteRenderer heroRenderer, bossRenderer;
    private Texture2D white;
    private AudioSource audioSource;
    private AudioClip selectClip, hitClip, hurtClip;
    private Phase phase = Phase.Menu;
    private Action action = Action.None;
    private float heroX, heroY, bossX, pillarTarget;
    private float heroHp, bossHp, stamina, elapsed, clock, phaseTimer, phaseDuration, cameraShake, damagePopupTimer;
    private int turn, pattern, bossStage, lastDamage;
    private bool heroHitApplied, bossHitApplied, guarded, won, soundOn;
    private string message = "";
    private float messageTimer;
    private ScoreScreen scoreScreen;
    private ScoreEntry[] scores = new ScoreEntry[0];
    private string scoreName = "", scoreStatus = "";
    private bool scoreBusy, drawingScoreScreen;
    private bool battleBusy;
    private string battleId, startKey, pendingAction;
    private TurnResponse turnResult;
    private Vector2 rankingScroll;

    private void Start()
    {
        for (int i = 0; i < 4; i++)
        {
            idle[i] = Resources.Load<Sprite>($"Sprites/HeroIdle/hero_idle_{i:00}");
            attack[i] = Resources.Load<Sprite>($"Sprites/HeroAttack/hero_attack_{i:00}");
            guard[i] = Resources.Load<Sprite>($"Sprites/HeroGuard/hero_guard_{i:00}");
        }
        for (int i = 0; i < 8; i++) bossArt[i] = Resources.Load<Sprite>($"Sprites/BossFacingLeft/boss_left_{i:00}");
        white = Texture2D.whiteTexture;
        audioSource = gameObject.AddComponent<AudioSource>();
        selectClip = Tone(550, .10f); hitClip = Tone(410, .18f); hurtClip = Tone(140, .24f);
        var backdrop = new GameObject("Generated Coastal Arena").AddComponent<SpriteRenderer>();
        backdrop.sprite = Resources.Load<Sprite>("Background/arena-waterfalls");
        backdrop.sortingOrder = -10;
        if (backdrop.sprite != null)
            backdrop.transform.localScale = new Vector3(20f / backdrop.sprite.bounds.size.x, 11.25f / backdrop.sprite.bounds.size.y, 1);
        heroRenderer = new GameObject("Rose Knight").AddComponent<SpriteRenderer>();
        heroRenderer.sortingOrder = 2;
        heroRenderer.transform.localScale = Vector3.one * .72f;
        bossRenderer = new GameObject("Bloomtide Crab").AddComponent<SpriteRenderer>();
        bossRenderer.sortingOrder = 1;
        bossRenderer.transform.localScale = Vector3.one * 1.02f;
        ResetFight(false);
    }

    private void ResetFight(bool play)
    {
        heroX = -2.2f; heroY = Floor; bossX = 4.8f;
        heroHp = bossHp = stamina = 100;
        elapsed = clock = phaseTimer = cameraShake = damagePopupTimer = 0;
        lastDamage = 0;
        turn = 1; pattern = 0; bossStage = 1;
        guarded = won = heroHitApplied = bossHitApplied = false;
        action = Action.None; phase = Phase.Menu;
        scoreScreen = ScoreScreen.None; scoreName = scoreStatus = "";
        message = play ? "행동을 선택하세요" : ""; messageTimer = play ? 1.4f : 0;
        UpdateVisuals();
        if (play && !battleBusy) StartCoroutine(StartBattle());
    }

    private IEnumerator StartBattle()
    {
        battleBusy = true;
        if (string.IsNullOrEmpty(startKey)) startKey = Guid.NewGuid().ToString("N");
        message = "서버에서 전투를 시작하는 중입니다.";
        using (var request = new UnityWebRequest(Api + "/battles", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Idempotency-Key", startKey);
            request.timeout = 6;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                var state = JsonUtility.FromJson<BattleStateResponse>(request.downloadHandler.text);
                battleId = state.battleId; startKey = null; pendingAction = null; turnResult = null;
                heroHp = state.heroHp; bossHp = state.bossHp; bossStage = state.bossStage;
                turn = state.turn; pattern = state.pattern; phase = Phase.Choice;
                message = "행동을 선택하세요"; messageTimer = 1.4f;
            }
            else { message = "C# 서버 연결을 확인해 주세요."; messageTimer = 99f; }
        }
        battleBusy = false;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return) && (phase == Phase.Menu || phase == Phase.Finished)) ResetFight(true);
        if (phase == Phase.Menu || phase == Phase.Finished) return;
        float dt = Mathf.Min(Time.deltaTime, .05f);
        clock += dt; elapsed += dt;
        messageTimer = Mathf.Max(0, messageTimer - dt);
        cameraShake = Mathf.Max(0, cameraShake - dt * 12f);
        damagePopupTimer = Mathf.Max(0, damagePopupTimer - dt);
        if (phase == Phase.Choice)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.J)) Choose(Action.Attack);
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.K)) Choose(Action.Guard);
            UpdateVisuals();
            return;
        }
        phaseTimer -= dt;
        float progress = Mathf.Clamp01(1 - phaseTimer / phaseDuration);
        if (phase == Phase.HeroAction)
        {
            if (action == Action.Attack)
            {
                if (progress > .47f && !heroHitApplied) HitBoss();
            }
            if (phaseTimer <= 0 && phase != Phase.Finished)
            {
                BeginBossTell();
            }
        }
        else if (phase == Phase.BossTell && phaseTimer <= 0)
        {
            phase = Phase.BossAction; phaseTimer = phaseDuration = .65f; bossHitApplied = false;
            Play(selectClip);
        }
        else if (phase == Phase.BossAction)
        {
            if (progress > .42f && !bossHitApplied) ResolveBossAttack();
            if (phaseTimer <= 0 && phase != Phase.Finished)
            { phase = Phase.BossRecover; phaseTimer = phaseDuration = .42f; }
        }
        else if (phase == Phase.BossRecover && phaseTimer <= 0) NextTurn();
        UpdateVisuals();
    }

    private void Choose(Action chosen)
    {
        if (phase != Phase.Choice) return;
        if (battleBusy) return;
        string selected = chosen == Action.Attack ? "attack" : "guard";
        if (pendingAction != null && pendingAction != selected)
        { message = "같은 행동으로 다시 시도하세요."; messageTimer = 2f; return; }
        StartCoroutine(SendTurn(chosen));
    }

    private IEnumerator SendTurn(Action chosen)
    {
        battleBusy = true;
        pendingAction = chosen == Action.Attack ? "attack" : "guard";
        message = "서버에서 턴을 판정하는 중입니다."; messageTimer = 99f;
        var payload = new TurnRequest { turn = turn, action = pendingAction };
        using (var request = new UnityWebRequest(Api + "/battles/" + battleId + "/turns", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload)));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 6;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            { message = "전송에 실패했습니다. 같은 행동으로 다시 시도하세요."; messageTimer = 99f; battleBusy = false; yield break; }
            turnResult = JsonUtility.FromJson<TurnResponse>(request.downloadHandler.text);
            if (turnResult.battleId != battleId || turnResult.turn != turn)
            { message = "서버 턴 정보가 일치하지 않습니다."; messageTimer = 99f; battleBusy = false; yield break; }
        }
        pendingAction = null;
        action = chosen;
        guarded = chosen == Action.Guard;
        heroHitApplied = false; phase = Phase.HeroAction;
        phaseTimer = phaseDuration = chosen == Action.Attack ? .86f : .42f;
        if (chosen == Action.Guard) stamina = Mathf.Min(100, stamina + 18);
        message = chosen == Action.Attack ? "검 공격 준비!" : "방어 태세!";
        messageTimer = .6f; Play(selectClip);
        battleBusy = false;
    }

    private void HitBoss()
    {
        heroHitApplied = true;
        int damage = turnResult.attackDamage;
        lastDamage = damage;
        damagePopupTimer = 1.15f;
        bossHp = turnResult.bossHp;
        message = "공격 적중! " + damage + " 피해"; messageTimer = 1.1f;
        cameraShake = .15f; Play(hitClip);
        if (turnResult.status == "won") { EndFight(true); return; }
        if (bossHp <= 50 && bossStage == 1)
        { bossStage = 2; message = "PHASE II · 성난 파도"; messageTimer = 1.7f; }
    }

    private void BeginBossTell()
    {
        phase = Phase.BossTell; phaseTimer = phaseDuration = .65f;
        pillarTarget = heroX;
        message = "⚠ " + PatternName(); messageTimer = .65f;
    }

    private void ResolveBossAttack()
    {
        bossHitApplied = true;
        HitHero(turnResult.bossDamage);
    }

    private void HitHero(int damage)
    {
        heroHp = turnResult.heroHp;
        cameraShake = guarded ? .08f : .24f; Play(guarded ? selectClip : hurtClip);
        message = guarded ? "방어 성공!" : "피격!"; messageTimer = .9f;
        if (turnResult.status == "lost") EndFight(false);
    }

    private void NextTurn()
    {
        turn = turnResult.nextTurn; pattern = (pattern + 1) % 3;
        turnResult = null;
        phase = Phase.Choice; action = Action.None;
        guarded = false; heroY = Floor;
        stamina = Mathf.Min(100, stamina + 13);
        message = "TURN " + turn + " · 행동 선택"; messageTimer = .9f;
    }

    private void EndFight(bool victory)
    {
        won = victory; phase = Phase.Finished;
        message = victory ? "승리!" : "패배"; messageTimer = 99f;
    }

    private string PatternName() => pattern == 0 ? "집게 휘두르기" : pattern == 1 ? "물결 분사" : "물기둥";
    private static int IdleFrame(float t)
    {
        float cycle = t % 2.35f;
        if (cycle < 1.45f) return 0;
        if (cycle < 1.73f) return 1;
        if (cycle < 1.84f) return 2;
        if (cycle < 2.13f) return 3;
        return 0;
    }

    private void UpdateVisuals()
    {
        float p = phaseDuration > 0 ? Mathf.Clamp01(1 - phaseTimer / phaseDuration) : 0;
        Sprite heroSprite = idle[IdleFrame(clock)];
        if (phase == Phase.HeroAction && action == Action.Attack)
            heroSprite = attack[p < .35f ? 0 : p < .53f ? 1 : p < .65f ? 2 : 3];
        else if (guarded) heroSprite = guard[phase == Phase.BossAction ? 2 : phase == Phase.BossTell ? 1 : 0];
        int bossFrame = phase == Phase.BossTell ? 4 : phase == Phase.BossAction ? 5 + Mathf.FloorToInt(clock * 12) % 2 : phase == Phase.BossRecover ? 7 : Mathf.FloorToInt(clock * 2) % 4;
        heroRenderer.sprite = heroSprite;
        heroRenderer.flipX = false;
        bossRenderer.sprite = bossArt[bossFrame];
        bossRenderer.flipX = false;
        heroRenderer.transform.position = new Vector3(heroX, heroY + 2.36f * .72f, 0);
        bossRenderer.transform.position = new Vector3(bossX, Floor + 2.36f * 1.02f + Mathf.Sin(clock * 2.8f) * .025f, 0);
        if (Camera.main != null) Camera.main.transform.position = new Vector3(Random.Range(-cameraShake, cameraShake), Random.Range(-cameraShake, cameraShake), -10);
    }

    private void OpenRanking()
    {
        scoreScreen = ScoreScreen.Ranking;
        scoreStatus = "랭킹을 불러오는 중입니다.";
        scores = new ScoreEntry[0];
        rankingScroll = Vector2.zero;
        StartCoroutine(LoadRanking());
    }

    private IEnumerator LoadRanking()
    {
        scoreBusy = true;
        using (var request = UnityWebRequest.Get(Api + "/scores"))
        {
            request.timeout = 4;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<ScoreResponse>(request.downloadHandler.text);
                scores = response != null && response.scores != null ? response.scores : new ScoreEntry[0];
                scoreStatus = scores.Length == 0 ? "아직 등록된 기록이 없습니다." : "적은 턴 수 · 같은 턴이면 남은 체력 순";
            }
            else scoreStatus = "C# 서버에 연결할 수 없습니다. 서버를 실행해 주세요.";
        }
        scoreBusy = false;
    }

    private IEnumerator SubmitScore()
    {
        scoreBusy = true;
        scoreStatus = "기록을 등록하는 중입니다.";
        byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new RegisterRequest { name = scoreName.Trim() }));
        using (var request = new UnityWebRequest(Api + "/battles/" + battleId + "/score", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 4;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                scoreBusy = false;
                OpenRanking();
                yield break;
            }
            scoreStatus = "등록하지 못했습니다. C# 서버 연결을 확인해 주세요.";
        }
        scoreBusy = false;
    }

    private void DrawScoreScreen()
    {
        if (scoreScreen == ScoreScreen.None) return;
        drawingScoreScreen = true;
        Box(18, 93, 1244, 502, new Color(.02f, .06f, .11f, .78f));
        Box(340, 139, 600, 440, new Color(.04f, .09f, .16f, .98f));
        Box(340, 139, 600, 2, new Color(.8f, .62f, .47f));
        Text(380, 160, 520, 25, scoreScreen == ScoreScreen.Register ? "REGISTER SCORE" : "HALL OF FAME", 16, new Color(.95f, .72f, .53f), true);
        Text(380, 191, 520, 55, scoreScreen == ScoreScreen.Register ? "기록 등록" : "랭킹", 39, new Color(1, .91f, .76f), true);
        if (scoreScreen == ScoreScreen.Register)
        {
            Text(390, 255, 500, 34, turn + "턴  ·  남은 체력 " + Mathf.CeilToInt(heroHp) + "%", 19, new Color(.87f, .9f, .9f), true);
            Text(390, 310, 110, 34, "이름", 17, new Color(1, .88f, .72f));
            var style = new GUIStyle(GUI.skin.textField) { fontSize = 21, alignment = TextAnchor.MiddleLeft };
            scoreName = GUI.TextField(new Rect(500, 310, 390, 38), scoreName, 12, style);
            Text(395, 366, 490, 38, scoreStatus, 14, new Color(.7f, .85f, .88f), true);
            if (Button(445, 435, 190, 58, scoreBusy ? "등록 중..." : "등록하기", 19, new Color(.8f, .39f, .35f)) && !scoreBusy)
            {
                if (string.IsNullOrWhiteSpace(scoreName)) scoreStatus = "이름을 입력하세요.";
                else StartCoroutine(SubmitScore());
            }
            if (Button(645, 435, 190, 58, "돌아가기", 18, new Color(.2f, .34f, .42f))) scoreScreen = ScoreScreen.None;
        }
        else
        {
            Rect view = new Rect(385, 255, 510, 235);
            Rect content = new Rect(0, 0, 485, Mathf.Max(235, scores.Length * 37));
            rankingScroll = GUI.BeginScrollView(view, rankingScroll, content);
            for (int i = 0; i < scores.Length; i++)
            {
                float y = i * 37;
                var entry = scores[i];
                Text(4, y, 40, 32, (i + 1).ToString(), 16, new Color(1, .8f, .55f));
                Text(48, y, 245, 32, entry.name, 16, new Color(1, .91f, .79f));
                Text(290, y, 85, 32, entry.turns + "턴", 16, Color.white, true);
                Text(380, y, 90, 32, entry.remainingHp + "%", 16, new Color(.7f, .85f, .88f), true);
            }
            GUI.EndScrollView();
            Text(385, 500, 510, 25, scoreStatus, 13, new Color(.7f, .85f, .88f), true);
            if (Button(550, 533, 180, 39, "돌아가기", 16, new Color(.2f, .34f, .42f))) scoreScreen = ScoreScreen.None;
        }
        drawingScoreScreen = false;
    }

    private void OnGUI()
    {
        float factor = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1280 * factor) / 2, (Screen.height - 720 * factor) / 2, 0), Quaternion.identity, Vector3.one * factor);
        Box(0, 0, 1280, 84, new Color(.045f, .09f, .15f, .98f));
        Text(24, 7, 360, 43, "✦  붉은 파도", 27, new Color(1, .89f, .72f));
        Text(75, 45, 300, 18, "T H E   C R I M S O N   T I D E", 10, new Color(.57f, .7f, .76f));
        if (Button(1150, 15, 106, 50, soundOn ? "♪  ON" : "♪  OFF", 18, new Color(.14f, .24f, .33f))) soundOn = !soundOn;
        Box(16, 91, 1248, 2, new Color(.57f, .65f, .69f));
        Box(16, 91, 2, 572, new Color(.57f, .65f, .69f));
        Box(1262, 91, 2, 572, new Color(.57f, .65f, .69f));
        Box(16, 661, 1248, 2, new Color(.57f, .65f, .69f));
        Box(18, 595, 1244, 66, new Color(.04f, .10f, .16f, .95f));
        Text(34, 104, 350, 21, "◆  ROSE KNIGHT", 13, new Color(1, .91f, .79f));
        Text(275, 104, 110, 21, Mathf.CeilToInt(heroHp) + " / 100", 12, Color.white);
        Text(895, 104, 350, 21, "BLOOMTIDE CRAB  ◆", 13, new Color(1, .91f, .79f));
        Text(1137, 104, 100, 21, Mathf.CeilToInt(bossHp) + "%", 12, Color.white);
        Bar(34, 132, 350, heroHp / 100f, new Color(.91f, .39f, .40f));
        Bar(896, 132, 350, bossHp / 100f, new Color(.96f, .64f, .44f));
        Text(34, 151, 120, 21, "STAMINA", 10, new Color(.75f, .87f, .89f));
        Bar(112, 157, 180, stamina / 100f, new Color(.54f, .85f, .89f));
        Text(1020, 153, 225, 21, bossStage == 1 ? "PHASE I · 잔잔한 파도" : "PHASE II · 성난 파도", 11, new Color(1, .88f, .72f));
        if (phase == Phase.BossTell)
        {
            float markerX = pattern == 2 ? (pillarTarget + 10) * 64 - 54 : (bossX + 10) * 64 - 270;
            Box(markerX, 530, pattern == 2 ? 108 : 270, 8, new Color(1, .23f, .26f, .8f));
        }
        if (messageTimer > 0 && phase != Phase.Menu && phase != Phase.Finished)
        { Box(410, 206, 460, 54, new Color(.05f, .1f, .17f, .85f)); Text(410, 206, 460, 54, message, 23, new Color(1, .91f, .79f), true); }
        if (damagePopupTimer > 0 && phase != Phase.Menu && phase != Phase.Finished)
        {
            float progress = 1f - damagePopupTimer / 1.15f;
            Color shadow = new Color(.18f, .04f, .07f, Mathf.Clamp01(damagePopupTimer / .35f));
            Color gold = new Color(1f, .93f, .69f, Mathf.Clamp01(damagePopupTimer / .35f));
            float y = 265f - progress * 65f;
            Text(766, y + 3, 250, 72, "-" + lastDamage, 48, shadow, true);
            Text(762, y, 250, 72, "-" + lastDamage, 48, gold, true);
        }
        Text(34, 599, 370, 25, "TURN " + turn.ToString("00") + " · " + (phase == Phase.Choice ? "PLAYER" : "BOSS"), 13, new Color(1, .89f, .72f));
        Text(34, 625, 360, 22, "다음 공격: " + PatternName(), 11, new Color(.7f, .85f, .88f));
        if (phase == Phase.Choice)
        {
            if (Button(480, 606, 195, 48, "⚔ 공격  [1]", 18, new Color(.2f, .34f, .42f))) Choose(Action.Attack);
            if (Button(685, 606, 195, 48, "◈ 방어  [2]", 18, new Color(.2f, .34f, .42f))) Choose(Action.Guard);
        }
        else Text(410, 606, 600, 48, phase == Phase.HeroAction ? "기사 행동 중..." : "보스 행동 중...", 17, new Color(.8f, .87f, .88f), true);
        Text(1020, 608, 215, 40, "✧  패턴을 보고 선택", 11, new Color(.7f, .84f, .86f));
        if (phase == Phase.Menu || phase == Phase.Finished)
        {
            Box(18, 93, 1244, 502, new Color(.02f, .06f, .11f, .67f));
            Box(390, 134, 500, 450, new Color(.04f, .09f, .16f, .97f));
            Box(390, 134, 500, 2, new Color(.8f, .62f, .47f));
            Text(440, 164, 400, 26, phase == Phase.Menu ? "A TURN-BASED BOSS BATTLE" : won ? "BOSS DEFEATED" : "BATTLE LOST", 17, new Color(.95f, .72f, .53f), true);
            Text(430, 211, 420, 83, phase == Phase.Menu ? "붉은 파도" : won ? "승리!" : "패배", 60, new Color(1, .91f, .76f), true);
            Text(410, 313, 460, 45, phase == Phase.Menu ? "공격(1–20 피해) 또는 방어를 선택하세요." : won ? "해안에 다시 평온이 찾아왔습니다." : "패턴을 살펴보고 다시 도전하세요.", 19, new Color(.87f, .9f, .9f), true);
            if (phase == Phase.Finished)
            {
                Text(420, 372, 215, 23, "소요 턴", 14, new Color(.68f, .76f, .79f), true);
                Text(645, 372, 215, 23, "남은 체력", 14, new Color(.68f, .76f, .79f), true);
                Text(420, 399, 215, 35, turn + "턴", 28, new Color(1, .91f, .76f), true);
                Text(645, 399, 215, 35, Mathf.CeilToInt(heroHp) + "%", 28, new Color(1, .91f, .76f), true);
            }
            if (phase == Phase.Menu)
            {
                if (Button(470, 460, 340, 59, battleBusy ? "서버 연결 중..." : "전투 시작  →", 22, new Color(.8f, .39f, .35f)) && !battleBusy) ResetFight(true);
                if (Button(520, 530, 240, 42, "랭킹 보기", 17, new Color(.2f, .34f, .42f))) OpenRanking();
            }
            else
            {
                if (won && Button(420, 471, 205, 59, "기록 등록", 20, new Color(.8f, .39f, .35f)))
                { scoreScreen = ScoreScreen.Register; scoreStatus = ""; }
                if (Button(won ? 635 : 420, 471, 205, 59, "랭킹 보기", 19, new Color(.2f, .34f, .42f))) OpenRanking();
                if (Button(won ? 540 : 635, won ? 540 : 471, 205, won ? 40 : 59, "다시 도전  ↻", 17, new Color(.2f, .34f, .42f))) ResetFight(true);
            }
        }
        DrawScoreScreen();
        Text(20, 678, 1240, 26, "CRIMSON TIDE  ·  TURN-BASED BOSS BATTLE", 10, new Color(.48f, .62f, .68f), true);
        GUI.matrix = previous;
    }

    private void Box(float x, float y, float w, float h, Color color)
    { Color previous = GUI.color; GUI.color = color; GUI.DrawTexture(new Rect(x, y, w, h), white); GUI.color = previous; }
    private void Bar(float x, float y, float w, float fraction, Color color)
    { Box(x, y, w, 13, new Color(.02f, .09f, .14f, .9f)); Box(x + 3, y + 3, Mathf.Max(0, (w - 6) * fraction), 7, color); }
    private void Text(float x, float y, float w, float h, string value, int size, Color color, bool center = false)
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold, alignment = center ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft };
        style.normal.textColor = color;
        GUI.Label(new Rect(x, y, w, h), value, style);
    }
    private bool Button(float x, float y, float w, float h, string value, int size, Color background)
    {
        var style = new GUIStyle(GUI.skin.button) { fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        style.normal.textColor = Color.white; style.hover.textColor = Color.white;
        Color previous = GUI.backgroundColor; GUI.backgroundColor = background;
        bool previousEnabled = GUI.enabled;
        if (scoreScreen != ScoreScreen.None && !drawingScoreScreen) GUI.enabled = false;
        bool pressed = GUI.Button(new Rect(x, y, w, h), value, style);
        GUI.enabled = previousEnabled;
        GUI.backgroundColor = previous; return pressed;
    }
    private void Play(AudioClip clip) { if (soundOn && clip != null) audioSource.PlayOneShot(clip, .24f); }
    private static AudioClip Tone(float frequency, float seconds)
    {
        const int sampleRate = 44100;
        int count = Mathf.CeilToInt(seconds * sampleRate);
        var samples = new float[count];
        for (int i = 0; i < count; i++) { float t = i / (float)sampleRate; samples[i] = Mathf.Sin(t * frequency * Mathf.PI * 2f) * Mathf.Exp(-t * 22f); }
        var clip = AudioClip.Create("Crimson Tide SFX", count, 1, sampleRate, false);
        clip.SetData(samples, 0); return clip;
    }
}
