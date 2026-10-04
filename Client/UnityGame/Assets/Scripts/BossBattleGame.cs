using System.Collections.Generic;
using UnityEngine;

/// <summary>A self-contained 2D boss fight. Images are animated by state, never by sliding a sheet crop.</summary>
public sealed class BossBattleGame : MonoBehaviour
{
    private const float Floor = -2.55f;
    private const float HeroScale = .72f;
    private const float BossScale = 1.02f;
    private readonly List<Wave> waves = new List<Wave>();
    private Sprite[] heroFrames = new Sprite[8];
    private Sprite[] heroIdleFrames = new Sprite[4];
    private Sprite[] heroRunFrames = new Sprite[6];
    private Sprite[] bossFrames = new Sprite[8];
    private SpriteRenderer heroRenderer, bossRenderer;
    private AudioSource sfxSource;
    private AudioClip slashClip, hurtClip, warningClip;
    private Sprite waveSprite;
    private Texture2D white;
    private float heroX, heroY, heroVelocity, bossX;
    private float heroHp, bossHp, stamina, time, animation, runAnimation, bossAnimation;
    private float attackTime, attackCooldown, invincible, dodgeTime, dodgeCooldown;
    private float bossTimer, warningX, screenShake;
    private int bossState, bossPattern, phase;
    private bool attackHit, guarding, facingRight, playing, finished, victory;
    private bool soundEnabled;
    private string message = "";
    private float messageTime;

    private sealed class Wave
    {
        public float x, y, speed, life;
        public GameObject visual;
    }

    private void Start()
    {
        for (int i = 0; i < 8; i++)
        {
            heroFrames[i] = Resources.Load<Sprite>($"Sprites/Hero/hero_{i:00}");
            bossFrames[i] = Resources.Load<Sprite>($"Sprites/BossFacingLeft/boss_left_{i:00}");
            if (heroFrames[i] == null || bossFrames[i] == null)
                Debug.LogError($"Missing generated sprite frame {i}. Run Crimson Tide/Create Boss Battle Scene.");
        }
        for (int i = 0; i < heroIdleFrames.Length; i++)
            heroIdleFrames[i] = Resources.Load<Sprite>($"Sprites/HeroIdle/hero_idle_{i:00}");
        for (int i = 0; i < heroRunFrames.Length; i++)
            heroRunFrames[i] = Resources.Load<Sprite>($"Sprites/HeroRun/hero_run_{i:00}");
        white = Texture2D.whiteTexture;
        sfxSource = gameObject.AddComponent<AudioSource>();
        slashClip = CreateTone(500f, .13f);
        hurtClip = CreateTone(145f, .22f);
        warningClip = CreateTone(260f, .15f);
        waveSprite = CreateWaveSprite();
        var backdrop = new GameObject("Generated Coastal Arena").AddComponent<SpriteRenderer>();
        backdrop.sprite = Resources.Load<Sprite>("Background/arena");
        backdrop.sortingOrder = -10;
        if (backdrop.sprite != null)
        {
            float width = backdrop.sprite.bounds.size.x;
            float height = backdrop.sprite.bounds.size.y;
            backdrop.transform.localScale = new Vector3(20f / width, 11.25f / height, 1);
        }
        heroRenderer = new GameObject("Rose Knight").AddComponent<SpriteRenderer>();
        heroRenderer.sortingOrder = 2;
        heroRenderer.transform.localScale = Vector3.one * HeroScale;
        bossRenderer = new GameObject("Bloomtide Crab").AddComponent<SpriteRenderer>();
        bossRenderer.sortingOrder = 1;
        bossRenderer.transform.localScale = Vector3.one * BossScale;
        ResetFight(false);
    }

    private void ResetFight(bool start)
    {
        foreach (var wave in waves) if (wave.visual != null) Destroy(wave.visual);
        waves.Clear();
        heroX = -5.6f; heroY = Floor; heroVelocity = 0;
        bossX = 5.3f; heroHp = 100; bossHp = 100; stamina = 100;
        time = animation = runAnimation = bossAnimation = attackTime = attackCooldown = invincible = dodgeTime = dodgeCooldown = 0;
        bossTimer = 2f; bossState = 0; bossPattern = -1; phase = 1;
        attackHit = guarding = finished = victory = false; facingRight = true; playing = start;
        message = start ? "결전 시작" : ""; messageTime = start ? 1.5f : 0;
        UpdateVisuals();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return) && (!playing || finished)) ResetFight(true);
        if (!playing || finished) return;
        float dt = Mathf.Min(Time.deltaTime, .04f);
        time += dt; animation += dt; bossAnimation += dt;
        attackTime = Mathf.Max(0, attackTime - dt);
        attackCooldown = Mathf.Max(0, attackCooldown - dt);
        dodgeTime = Mathf.Max(0, dodgeTime - dt);
        dodgeCooldown = Mathf.Max(0, dodgeCooldown - dt);
        invincible = Mathf.Max(0, invincible - dt);
        screenShake = Mathf.Max(0, screenShake - dt * 10);
        messageTime = Mathf.Max(0, messageTime - dt);
        float axis = 0;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) axis--;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) axis++;
        guarding = Input.GetKey(KeyCode.K) && dodgeTime <= 0 && attackTime <= 0 && stamina > 0;
        stamina = Mathf.Clamp(stamina + (guarding ? -13f : 23f) * dt, 0, 100);
        if (axis != 0 && !guarding) { facingRight = axis > 0; heroX += axis * (dodgeTime > 0 ? 12f : 6.4f) * dt; }
        if (axis != 0 && !guarding && heroY <= Floor + .01f) runAnimation += dt * 12f;
        else runAnimation = 0;
        heroX = Mathf.Clamp(heroX, -9f, bossX - 1.45f);
        if ((Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) && heroY <= Floor + .01f && !guarding)
            heroVelocity = 10.8f;
        heroVelocity -= 25f * dt; heroY = Mathf.Max(Floor, heroY + heroVelocity * dt);
        if (heroY <= Floor) heroVelocity = 0;
        if (Input.GetKeyDown(KeyCode.LeftShift) && dodgeCooldown <= 0 && stamina >= 24 && !guarding)
        {
            stamina -= 24; dodgeTime = .32f; dodgeCooldown = .85f;
            heroX = Mathf.Clamp(heroX + (facingRight ? 1.4f : -1.4f), -9f, bossX - 1.45f);
        }
        if (Input.GetKeyDown(KeyCode.J) && attackCooldown <= 0 && !guarding && dodgeTime <= 0)
        {
            attackTime = .42f; attackCooldown = .53f; attackHit = false;
            PlaySound(slashClip);
        }
        if (attackTime > .08f && attackTime < .28f && !attackHit && facingRight && bossX - heroX < 3.1f && heroY < Floor + 1.6f)
        {
            attackHit = true; BossDamage(bossState == 1 ? 12 : 8);
        }
        UpdateBoss(dt);
        UpdateWaves(dt);
        UpdateVisuals();
    }

    private void UpdateBoss(float dt)
    {
        bossTimer -= dt;
        if (bossState == 0 && bossTimer <= 0)
        {
            bossPattern = (bossPattern + 1) % 3;
            bossState = 1; bossTimer = phase == 1 ? .95f : .68f;
            warningX = Mathf.Clamp(heroX + .5f, -8f, 7f);
            ShowMessage(bossPattern == 0 ? "집게 공격!" : bossPattern == 1 ? "물결 분사!" : "물기둥 주의!", .9f);
            PlaySound(warningClip);
        }
        else if (bossState == 1 && bossTimer <= 0)
        {
            bossState = 2; bossTimer = bossPattern == 1 ? 1f : .48f;
            if (bossPattern == 0 && heroX > bossX - 4.2f && heroY < Floor + 1.6f) HeroDamage(phase == 1 ? 17 : 23);
            if (bossPattern == 1)
            {
                for (int i = 0; i < (phase == 1 ? 3 : 5); i++)
                    SpawnWave(bossX - 1.6f, Floor + .65f + i * .55f, -6.8f - i * .25f);
            }
            if (bossPattern == 2 && Mathf.Abs(heroX - warningX) < 1.4f && heroY < Floor + 1.7f) HeroDamage(phase == 1 ? 16 : 22);
            screenShake = .22f;
        }
        else if (bossState == 2 && bossTimer <= 0)
        {
            bossState = 3; bossTimer = phase == 1 ? .85f : .55f;
        }
        else if (bossState == 3 && bossTimer <= 0)
        {
            bossState = 0; bossTimer = phase == 1 ? .8f : .45f;
        }
    }

    private void SpawnWave(float x, float y, float speed)
    {
        var visual = new GameObject("Water Projectile").AddComponent<SpriteRenderer>();
        visual.sprite = waveSprite;
        visual.sortingOrder = 3; visual.transform.localScale = Vector3.one * .75f;
        waves.Add(new Wave { x = x, y = y, speed = speed, life = 3f, visual = visual.gameObject });
    }

    private static Sprite CreateWaveSprite()
    {
        var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        for (int py = 0; py < 32; py++) for (int px = 0; px < 32; px++)
        {
            float radius = Vector2.Distance(new Vector2(px, py), new Vector2(15.5f, 15.5f));
            texture.SetPixel(px, py, radius < 14 ? new Color(.56f, .86f, 1f, Mathf.Clamp01((15 - radius) / 3f)) : Color.clear);
        }
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, 32, 32), new Vector2(.5f, .5f), 32);
    }

    private void UpdateWaves(float dt)
    {
        for (int i = waves.Count - 1; i >= 0; i--)
        {
            var wave = waves[i]; wave.x += wave.speed * dt; wave.life -= dt;
            wave.visual.transform.position = new Vector3(wave.x, wave.y, 0);
            if (Mathf.Abs(wave.x - heroX) < .65f && Mathf.Abs(wave.y - (heroY + 1f)) < .9f)
            { HeroDamage(phase == 1 ? 10 : 14); wave.life = 0; }
            if (wave.life <= 0 || wave.x < -11f) { Destroy(wave.visual); waves.RemoveAt(i); }
        }
    }

    private void HeroDamage(int amount)
    {
        if (invincible > 0 || dodgeTime > 0 || finished) return;
        if (guarding) { stamina = Mathf.Max(0, stamina - amount * 1.6f); amount = Mathf.CeilToInt(amount * .2f); }
        heroHp = Mathf.Max(0, heroHp - amount); invincible = .6f; screenShake = .25f;
        PlaySound(hurtClip);
        if (heroHp <= 0) EndFight(false);
    }

    private void BossDamage(int amount)
    {
        bossHp = Mathf.Max(0, bossHp - amount); screenShake = .12f;
        if (bossHp <= 0) EndFight(true);
        else if (bossHp <= 50 && phase == 1)
        {
            phase = 2; bossState = 0; bossTimer = 2f; ShowMessage("PHASE II · 성난 파도", 2f);
        }
    }

    private void EndFight(bool won)
    {
        finished = true; victory = won; ShowMessage(won ? "승리!" : "패배", 100f);
    }

    private void ShowMessage(string text, float duration) { message = text; messageTime = duration; }

    private void PlaySound(AudioClip clip)
    {
        if (soundEnabled && clip != null) sfxSource.PlayOneShot(clip, .24f);
    }

    private static AudioClip CreateTone(float frequency, float seconds)
    {
        const int sampleRate = 44100;
        int count = Mathf.CeilToInt(seconds * sampleRate);
        float[] samples = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)sampleRate;
            samples[i] = Mathf.Sin(t * frequency * Mathf.PI * 2f) * Mathf.Exp(-t * 22f);
        }
        var clip = AudioClip.Create("Crimson Tide SFX", count, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void UpdateVisuals()
    {
        bool moving = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow);
        Sprite heroSprite = guarding ? heroFrames[7] : attackTime > .28f ? heroFrames[4] : attackTime > .15f ? heroFrames[5] : attackTime > 0 ? heroFrames[6] :
            heroY > Floor + .05f ? heroFrames[2] : moving ? heroRunFrames[Mathf.FloorToInt(runAnimation) % heroRunFrames.Length] : heroIdleFrames[IdleFrame(animation)];
        int bossFrame = bossState == 1 ? 4 : bossState == 2 ? 5 + Mathf.FloorToInt(bossAnimation * 8) % 2 : bossState == 3 ? 7 : Mathf.FloorToInt(bossAnimation * 2) % 4;
        heroRenderer.sprite = heroSprite; bossRenderer.sprite = bossFrames[bossFrame];
        heroRenderer.flipX = !facingRight;
        bossRenderer.flipX = false;
        heroRenderer.color = invincible > 0 && Mathf.FloorToInt(time * 16) % 2 == 0 ? new Color(1, 1, 1, .45f) : Color.white;
        bossRenderer.color = phase == 2 ? new Color(1, .83f, .83f) : Color.white;
        heroRenderer.transform.position = new Vector3(heroX, heroY + 2.36f * HeroScale, 0);
        bossRenderer.transform.position = new Vector3(bossX, Floor + 2.36f * BossScale + Mathf.Sin(time * 3.1f) * .035f, 0);
        Camera.main.transform.position = new Vector3(Random.Range(-screenShake, screenShake), Random.Range(-screenShake, screenShake), -10);
    }

    private static int IdleFrame(float seconds)
    {
        float cycle = seconds % 2.35f;
        if (cycle < 1.45f) return 0;
        if (cycle < 1.73f) return 1;
        if (cycle < 1.84f) return 2;
        if (cycle < 2.13f) return 3;
        return 0;
    }

    private void OnGUI()
    {
        float factor = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
        var old = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1280 * factor) / 2, (Screen.height - 720 * factor) / 2, 0), Quaternion.identity, Vector3.one * factor);
        DrawBox(0, 0, 1280, 84, new Color(.045f, .09f, .15f, .98f));
        Label(24, 7, 360, 43, "✦  붉은 파도", 27, new Color(1, .89f, .72f));
        Label(75, 45, 300, 18, "T H E   C R I M S O N   T I D E", 10, new Color(.57f, .7f, .76f));
        if (Button(1150, 15, 106, 50, soundEnabled ? "♪  ON" : "♪  OFF", 18, new Color(.14f, .24f, .33f))) soundEnabled = !soundEnabled;
        DrawBox(16, 91, 1248, 2, new Color(.57f, .65f, .69f));
        DrawBox(16, 91, 2, 572, new Color(.57f, .65f, .69f));
        DrawBox(1262, 91, 2, 572, new Color(.57f, .65f, .69f));
        DrawBox(16, 661, 1248, 2, new Color(.57f, .65f, .69f));
        DrawBox(18, 604, 1244, 57, new Color(.04f, .1f, .16f, .94f));
        Label(34, 104, 350, 21, "◆  ROSE KNIGHT", 13, new Color(1, .91f, .79f));
        Label(275, 104, 110, 21, Mathf.CeilToInt(heroHp) + " / 100", 12, Color.white);
        Label(895, 104, 350, 21, "BLOOMTIDE CRAB  ◆", 13, new Color(1, .91f, .79f));
        Label(1137, 104, 100, 21, Mathf.CeilToInt(bossHp) + "%", 12, Color.white);
        DrawBar(34, 132, 350, heroHp / 100f, new Color(.91f, .39f, .40f));
        DrawBar(896, 132, 350, bossHp / 100f, new Color(.96f, .64f, .44f));
        Label(34, 151, 120, 21, "STAMINA", 10, new Color(.75f, .87f, .89f));
        DrawBar(112, 157, 180, stamina / 100f, new Color(.54f, .85f, .89f));
        Label(1030, 153, 215, 21, phase == 1 ? "PHASE I · 잔잔한 파도" : "PHASE II · 성난 파도", 11, new Color(1, .88f, .72f));
        if (bossState == 1 && playing && !finished)
        {
            float x = bossPattern == 2 ? (warningX + 10) * 64 : (bossX + 10) * 64 - 270;
            DrawBox(x, 528, bossPattern == 2 ? 105 : 270, 12, new Color(1, .24f, .25f, .75f));
        }
        if (messageTime > 0 && playing && !finished)
        {
            DrawBox(400, 205, 480, 58, new Color(.05f, .1f, .17f, .83f));
            Label(400, 205, 480, 58, message, 24, new Color(1, .91f, .79f), true);
        }
        if (!playing || finished)
        {
            DrawBox(18, 93, 1244, 511, new Color(.02f, .06f, .11f, .67f));
            DrawBox(390, 134, 500, 450, new Color(.04f, .09f, .16f, .97f));
            DrawBox(390, 134, 500, 2, new Color(.8f, .62f, .47f));
            Label(440, 164, 400, 26, finished ? (victory ? "BOSS DEFEATED" : "BATTLE LOST") : "A ONE BOSS BATTLE", 17, new Color(.95f, .72f, .53f), true);
            Label(430, 211, 420, 83, finished ? (victory ? "승리!" : "패배") : "붉은 파도", 60, new Color(1, .91f, .76f), true);
            Label(410, 313, 460, 45, finished ? (victory ? "해안에 다시 평온이 찾아왔습니다." : "패턴을 살펴보고 다시 도전하세요.") : "꽃이 피는 해안에서 바다의 수호자와 맞서세요.", 19, new Color(.87f, .9f, .9f), true);
            if (finished)
            {
                Label(420, 372, 215, 23, "클리어 시간", 14, new Color(.68f, .76f, .79f), true);
                Label(645, 372, 215, 23, "남은 체력", 14, new Color(.68f, .76f, .79f), true);
                Label(420, 399, 215, 35, time.ToString("0.0") + "초", 28, new Color(1, .91f, .76f), true);
                Label(645, 399, 215, 35, Mathf.CeilToInt(heroHp) + "%", 28, new Color(1, .91f, .76f), true);
            }
            if (Button(470, 473, 340, 67, finished ? "다시 도전  ↻" : "전투 시작  →", 23, new Color(.8f, .39f, .35f))) ResetFight(true);
        }
        Label(36, 613, 885, 40, "A / D   이동       SPACE   점프       J   공격       K   방어       SHIFT   회피", 16, new Color(.85f, .9f, .9f));
        Label(885, 613, 360, 40, "✧  보스의 움직임을 보고 공격하세요.", 12, new Color(.68f, .78f, .79f));
        Label(20, 678, 1240, 26, "CRIMSON TIDE  ·  BOSS BATTLE PROTOTYPE", 10, new Color(.48f, .62f, .68f), true);
        GUI.matrix = old;
    }

    private bool Button(float x, float y, float w, float h, string value, int size, Color background)
    {
        var style = new GUIStyle(GUI.skin.button) { fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        style.normal.textColor = Color.white;
        style.hover.textColor = Color.white;
        Color old = GUI.backgroundColor;
        GUI.backgroundColor = background;
        bool clicked = GUI.Button(new Rect(x, y, w, h), value, style);
        GUI.backgroundColor = old;
        return clicked;
    }

    private void DrawBox(float x, float y, float w, float h, Color color)
    {
        Color old = GUI.color; GUI.color = color; GUI.DrawTexture(new Rect(x, y, w, h), white); GUI.color = old;
    }

    private void DrawBar(float x, float y, float w, float fraction, Color color)
    {
        DrawBox(x, y, w, 13, new Color(.02f, .09f, .14f, .9f));
        DrawBox(x + 3, y + 3, Mathf.Max(0, (w - 6) * fraction), 7, color);
    }

    private void Label(float x, float y, float w, float h, string value, int size, Color color, bool centered = false)
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold, alignment = centered ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft };
        style.normal.textColor = color;
        GUI.Label(new Rect(x, y, w, h), value, style);
    }
}
