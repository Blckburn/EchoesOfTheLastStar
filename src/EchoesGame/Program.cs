using Raylib_cs;
using System;
using System.Numerics;
using System.Collections.Generic;
using EchoesGame.Infra;
using System.Linq;

namespace EchoesGame
{
internal static class Program
{
    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;
    private const int TargetFps = 60;

    private static void Main()
    {
        Raylib.InitWindow(WindowWidth, WindowHeight, "Echoes of the Last Star — Prototype");
        Raylib.SetTargetFPS(TargetFps);
        Raylib.SetMouseCursor(MouseCursor.Crosshair);
        EchoesGame.Infra.Assets.Init(System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "textures"));
        Game.Fonts.Init(System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "ui_font.ttf"));

        var player = new Game.Player(new Vector2(0, 0));
        var enemySpawner = new Game.EnemySpawner();
        var projectilePool = new Game.ProjectilePool(capacity: 512);
        var xpSystem = new Game.XPSystem();
        var xpOrbs = new Game.XPOrbPool(capacity: 512);
        var draft = new Game.LevelUpDraft();
        var orbitals = new Game.OrbitalsSystem();
        var pacts = new Game.PactOverlay();
        var boss = new Game.BossManager();
        var bossShots = new Game.EnemyProjectilePool(256);
        bool draftOpen = false;
        var camera = new Camera2D
        {
            Target = player.Position,
            Offset = new Vector2(WindowWidth / 2f, WindowHeight / 2f),
            Rotation = 0,
            Zoom = 1f
        };
        bool paused = false;
        bool pactsMenuOpen = false;
        float pactsMenuDebounce = 0f;
        bool gameOver = false;
        float elapsed = 0f;
        int score = 0;
        EchoesGame.Infra.Analytics.Init();
        EchoesGame.Infra.Analytics.Log("start_run", new System.Collections.Generic.Dictionary<string, object>());
        int lastPending = 0;
        float nextPactAt = 30f;
        float nextBossAt = 60f; // first mini-boss at 1:00 for testing

        float slowMoTimer = 0f;
        // Reward on boss death: big XP burst + brief slow-mo and score bonus
            boss.OnDeath = (Vector2 pos) => {
            for (int i = 0; i < 16; i++)
            {
                float ox = Raylib.GetRandomValue(-60, 60);
                float oy = Raylib.GetRandomValue(-60, 60);
                xpOrbs.Spawn(new Vector2(pos.X + ox, pos.Y + oy), Raylib.GetRandomValue(3, 6));
            }
            score += 250;
            slowMoTimer = 0.6f;
                Infra.Analytics.Log("boss_kill", new System.Collections.Generic.Dictionary<string, object>{{"t", Raylib.GetTime()}});
                Game.WeaponPickupSystem.EnsureBossDrop(pos);
        };
        while (!Raylib.WindowShouldClose())
        {
            float dtRaw = Raylib.GetFrameTime();
            if (slowMoTimer > 0f) { slowMoTimer -= dtRaw; if (slowMoTimer < 0f) slowMoTimer = 0f; }
            float dt = slowMoTimer > 0f ? dtRaw * 0.4f : dtRaw;

            if (Raylib.IsKeyPressed(KeyboardKey.P)) paused = !paused;
            if (!draftOpen && !pacts.Opened && !gameOver)
            {
                if (Raylib.IsKeyPressed(KeyboardKey.O))
                {
                    bool opening = !pactsMenuOpen;
                    pactsMenuOpen = !pactsMenuOpen;
                    if (opening) pactsMenuDebounce = 0.2f;
                }
            }
            if (pactsMenuDebounce > 0f) pactsMenuDebounce -= dt;
            if (paused || draftOpen || gameOver || pacts.Opened || pactsMenuOpen)
            {
                // Draw only
                Raylib.BeginDrawing();
                Raylib.ClearBackground(Color.Black);
                camera.Target = player.Position;
                Raylib.BeginMode2D(camera);
                DrawFloorTiles();
                DrawWorldWalls();
                enemySpawner.Draw(player.Position);
                // weapon pickups (if any)
                Game.WeaponPickupSystem.DrawStatic();
                xpOrbs.Draw();
                projectilePool.Draw();
                player.Draw(camera);
                Game.FloatingTextSystem.Draw();
                Game.KeystoneRuntime.DrawOverlayWorld();
                Raylib.EndMode2D();
                DrawHud(player, paused, projectilePool.ActiveCount, enemySpawner.Count, xpSystem, elapsed, score);
                Game.KeystoneRuntime.DrawOverlay();
                // Next pact countdown (top-right)
                float pactRemainA = MathF.Max(0f, nextPactAt - elapsed);
                string pactTxtA = $"Next Pact: {pactRemainA:0}s";
                int pxA = Raylib.GetScreenWidth() - 200;
                int pyA = 12;
                Game.Fonts.DrawText(pactTxtA, pxA, pyA, 20, Color.Red);
                if (paused) {
                    Game.PactRuntime.DrawPermanentPanel(Raylib.GetScreenWidth() - 360 - 16, 16, 360);
                    // Center PAUSED label
                    int w = Raylib.GetScreenWidth();
                    int h = Raylib.GetScreenHeight();
                    string t = "PAUSED (P)";
                    int size = 36; int tw = Raylib.MeasureText(t, size);
                    int tx = (w - tw) / 2; int ty = h / 2 - size;
                    Raylib.DrawText(t, tx+1, ty+1, size, Color.Black);
                    Raylib.DrawText(t, tx, ty, size, Color.Gold);
                }
                if (draftOpen)
                {
                    draft.Draw();
                    // Handle input 1..3 or click
                    if (Raylib.IsKeyPressed(KeyboardKey.One)) { draft.Apply(0, player); xpSystem.ConsumePending(); draftOpen = false; }
                    else if (Raylib.IsKeyPressed(KeyboardKey.Two)) { draft.Apply(1, player); xpSystem.ConsumePending(); draftOpen = false; }
                    else if (Raylib.IsKeyPressed(KeyboardKey.Three)) { draft.Apply(2, player); xpSystem.ConsumePending(); draftOpen = false; }
                    else if (Raylib.IsMouseButtonPressed(MouseButton.Left))
                    {
                        int i = draft.HitTest(Raylib.GetMousePosition());
                        if (i >= 0) { draft.Apply(i, player); xpSystem.ConsumePending(); draftOpen = false; }
                    }
                }
                if (pacts.Opened)
                {
                    pacts.Draw();
                    if (Raylib.IsKeyPressed(KeyboardKey.One)) { pacts.Apply(0, ref player, enemySpawner); }
                    else if (Raylib.IsKeyPressed(KeyboardKey.Two)) { pacts.Apply(1, ref player, enemySpawner); }
                    else if (Raylib.IsMouseButtonPressed(MouseButton.Left))
                    {
                        int i = pacts.HitTest(Raylib.GetMousePosition());
                        if (i >= 0) pacts.Apply(i, ref player, enemySpawner);
                    }
                }
                if (pactsMenuOpen)
                {
                    Game.PactsMenuOverlay.Draw();
                    if (pactsMenuDebounce <= 0f && (Raylib.IsKeyPressed(KeyboardKey.O) || Raylib.IsKeyPressed(KeyboardKey.Escape)))
                    {
                        pactsMenuOpen = false;
                    }
                }
                if (gameOver)
                {
                    DrawGameOver();
                    if (Raylib.IsKeyPressed(KeyboardKey.R)) { ResetGame(ref player, enemySpawner, ref projectilePool, ref xpOrbs, ref xpSystem, ref draftOpen, ref gameOver, ref elapsed, ref score); }
                }
                Raylib.EndDrawing();
                continue;
            }

            // Update
            elapsed += dt;
            player.TickDamageIFrames(dt);
            Game.PactRuntime.Update(dt);
            Game.KeystoneRuntime.Update(dt);
            Game.FloatingTextSystem.Update(dt);
            player.Update(dt, projectilePool, camera);
            enemySpawner.Update(dt, player.Position, camera);
            boss.Update(dt, player.Position, bossShots);
            if (Game.KeystoneRuntime.IsActiveGravityWell && boss.Alive)
            {
                Game.KeystoneRuntime.SetGravityAnchor(boss.Position);
            }
            projectilePool.Update(dt);
            bossShots.Update(dt, ref player);
            xpOrbs.Update(dt, player.Position, xpSystem, player.XPMagnetMultiplier);
            orbitals.Update(dt, player.Position, enemySpawner.Enemies);
            Game.Collision.Resolve(projectilePool, enemySpawner.Enemies, (enemy) => { xpOrbs.Spawn(enemy.Position, Raylib.GetRandomValue(1,2)); score += enemy.IsElite ? 25 : 10; }, boss);
            // Apply dynamic bonuses from timed pacts
            if (Game.PactRuntime.EliteChanceBonus > 0f) enemySpawner.SetEliteChanceBonus(Game.PactRuntime.EliteChanceBonus); else enemySpawner.SetEliteChanceBonus(0f);

            // Heal on level up (10% max)
            if (xpSystem.PendingChoices > lastPending)
            {
                player.HealPercent(0.10f);
                lastPending = xpSystem.PendingChoices;
            }

            // Contact damage
            foreach (var e in enemySpawner.Enemies)
            {
                if (!e.Alive) continue;
                if (Raylib.CheckCollisionRecs(new Rectangle(player.Position.X - 10, player.Position.Y - 10, 20, 20), e.GetBounds()))
                {
                    player.TakeDamage(e.ContactDamage);
                }
            }
            // GravityWell pull on player
            Vector2 gfp = Game.KeystoneRuntime.GetGravityForce(player.Position);
            if (gfp.LengthSquared() > 0f) player.ApplyExternalForce(gfp, dt * 0.35f);
            if (boss.Alive)
            {
                if (Raylib.CheckCollisionRecs(new Rectangle(player.Position.X - 10, player.Position.Y - 10, 20, 20), boss.Bounds))
                {
                    player.TakeDamage(30f);
                }
            }
            if (player.IsDead)
            {
                if (!gameOver)
                {
                    Infra.Analytics.Log("death", new System.Collections.Generic.Dictionary<string, object>{{"t", Raylib.GetTime()},{"cause", player.LastDamageCause}});
                }
                gameOver = true;
            }

            if (!draftOpen && xpSystem.PendingChoices > 0)
            {
                draft.Open();
                draftOpen = true;
            }
            if (!pacts.Opened && elapsed >= nextPactAt)
            {
                pacts.Open();
                nextPactAt += 45f;
            }
            // Boss spawn
            if (!boss.Alive && elapsed >= nextBossAt)
            {
                boss.SpawnNear(camera);
                var k = (Game.KeystoneType)Raylib.GetRandomValue(0, 1);
                Game.KeystoneRuntime.Activate(k, 35f);
                if (k == Game.KeystoneType.GravityWell) Game.KeystoneRuntime.SetGravityAnchor(boss.Position);
                Infra.Analytics.Log("boss_spawn", new System.Collections.Generic.Dictionary<string, object>{{"t", Raylib.GetTime()}});
                nextBossAt += 180f;
            }

            // Draw
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);

            camera.Target = player.Position;
            Raylib.BeginMode2D(camera);
            DrawFloorTiles();
            DrawWorldWalls();
            enemySpawner.Draw(player.Position);
            boss.Draw();
            xpOrbs.Draw();
            projectilePool.Draw();
            bossShots.Draw();
            orbitals.Draw();
            player.Draw(camera);
            Game.FloatingTextSystem.Draw();
            Game.KeystoneRuntime.DrawOverlayWorld();
            Raylib.EndMode2D();
            
            DrawHud(player, paused, projectilePool.ActiveCount, enemySpawner.Count, xpSystem, elapsed, score);
            Game.KeystoneRuntime.DrawOverlay();
            if (paused)
            {
                int w = Raylib.GetScreenWidth();
                int h = Raylib.GetScreenHeight();
                string t = "PAUSED (P)";
                int size = 36;
                int tw = Raylib.MeasureText(t, size);
                int tx = (w - tw) / 2;
                int ty = h / 2 - size;
                Raylib.DrawText(t, tx+1, ty+1, size, Color.Black);
                Raylib.DrawText(t, tx, ty, size, Color.Gold);
            }
            // Next pact countdown (top-right)
            float pactRemainB = MathF.Max(0f, nextPactAt - elapsed);
            string pactTxtB = $"Next Pact: {pactRemainB:0}s";
            int pxB = Raylib.GetScreenWidth() - 200;
            int pyB = 12;
            Game.Fonts.DrawText(pactTxtB, pxB, pyB, 20, Color.Red);
            if (paused || pactsMenuOpen) Game.PactRuntime.DrawPermanentPanel(Raylib.GetScreenWidth() - 360 - 16, 16, 360);
            boss.DrawBossHud();
            // Pact panel is drawn within DrawHud
            if (draftOpen) draft.Draw();

            Raylib.EndDrawing();
        }

        EchoesGame.Infra.Assets.Dispose();
        Raylib.CloseWindow();
    }

    private static void DrawGrid()
    {
        const int step = 256;
        int halfW = Config.WorldWidth / 2;
        int halfH = Config.WorldHeight / 2;
        var origin = new Vector2(0, 0);
        Raylib.DrawCircleV(origin, 6, Color.DarkGray);
        for (int x = -halfW; x <= halfW; x += step)
        {
            Raylib.DrawLine(x, -halfH, x, halfH, Color.DarkGray);
        }
        for (int y = -halfH; y <= halfH; y += step)
        {
            Raylib.DrawLine(-halfW, y, halfW, y, Color.DarkGray);
        }
    }

    private static void DrawWorldBounds()
    {
        int halfW = Config.WorldWidth / 2;
        int halfH = Config.WorldHeight / 2;
        var rec = new Rectangle(-halfW, -halfH, Config.WorldWidth, Config.WorldHeight);
        Raylib.DrawRectangleLinesEx(rec, 4, Color.DarkGray);
    }

    private static void DrawFloorTiles()
    {
        if (TryGetTex(out var tile, "floor_tile.png", "floor.png"))
        {
            int halfW = Config.WorldWidth / 2;
            int halfH = Config.WorldHeight / 2;
            for (int y = -halfH; y < halfH; y += tile.Height)
            {
                for (int x = -halfW; x < halfW; x += tile.Width)
                {
                    Raylib.DrawTexture(tile, x, y, Color.White);
                }
            }
        }
        else
        {
            DrawGrid();
        }
    }

    private static void DrawWorldWalls()
    {
        if (TryGetTex(out var wall, "wall_tile.png", "wall.png", "walls_tile.png"))
        {
            int halfW = Config.WorldWidth / 2;
            int halfH = Config.WorldHeight / 2;
            for (int x = -halfW; x < halfW; x += wall.Width)
            {
                Raylib.DrawTexture(wall, x, -halfH - wall.Height, Color.White);
                Raylib.DrawTexture(wall, x, halfH, Color.White);
            }
            for (int y = -halfH; y < halfH; y += wall.Height)
            {
                Raylib.DrawTexture(wall, -halfW - wall.Width, y, Color.White);
                Raylib.DrawTexture(wall, halfW, y, Color.White);
            }
        }
        else
        {
            DrawWorldBounds();
        }
    }

    private static bool TryGetTex(out Texture2D tex, params string[] candidates)
    {
        foreach (var name in candidates)
        {
            if (Assets.TryGet(name, out tex)) return true;
        }
        tex = default;
        return false;
    }

    private static void DrawHud(Game.Player player, bool paused, int bullets, int enemies, Game.XPSystem xp, float elapsedSeconds = 0f, int score = 0)
    {
        int y = 16;
        Game.Fonts.DrawText($"FPS: {Raylib.GetFPS()}  Time: {elapsedSeconds:0}s  Score: {score}", 16, y, 20, Color.RayWhite); y += 22;
        Game.Fonts.DrawText($"Dash CD: {player.DashCooldownRemaining:0.00}s", 16, y, 20, Color.RayWhite); y += 22;
        Game.Fonts.DrawText($"Bullets: {bullets}  Enemies: {enemies}", 16, y, 20, Color.RayWhite); y += 22;
        // Dash readiness bar (0..1)
        float readiness = 1f - (player.DashCooldownRemaining / player.DashCooldownTotal);
        if (readiness < 0f) readiness = 0f; if (readiness > 1f) readiness = 1f;
        DrawBar(16, y + 6, 256, 16, readiness);
        y += 28;
        // HP bar (red variant if present) + numeric HP display
        DrawBar(16, y + 6, 256, 16, MathF.Max(0f, player.HP / player.MaxHP), "ui_bar_bg.png", TryHasRed() ? "ui_bar_fg_red.png" : "ui_bar_fg.png");
        Game.Fonts.DrawText($"HP: {player.HP:0}/{player.MaxHP:0}", 280, y, 20, Color.RayWhite);
        y += 28;
        // XP bar and level
        float xpNorm = xp.ProgressNormalized;
        DrawBar(16, y + 6, 256, 16, xpNorm);
        Game.Fonts.DrawText($"LVL: {xp.Level}", 280, y, 20, Color.RayWhite);
        y += 28;
        // Active modifiers (stacked, per line)
        if (player.ModStacks.Count > 0)
        {
            Game.Fonts.DrawText("Mods:", 16, y, 20, Color.RayWhite); y += 22;
            foreach (var kv in player.ModStacks)
            {
                string line = kv.Key switch {
                    Game.Player.ModType.BulletSpeed => $"+{kv.Value*15}% bullet speed",
                    Game.Player.ModType.ShootInterval => $"+{kv.Value*10}% attack speed",
                    Game.Player.ModType.BulletDamage => $"+{kv.Value*20}% damage",
                    Game.Player.ModType.MoveSpeed => $"+{kv.Value*10}% move speed",
                    Game.Player.ModType.XPMagnet => $"+{kv.Value*20}% XP magnet",
                    Game.Player.ModType.DashCooldown => $"-{kv.Value*10}% dash cooldown",
                    _ => $"x{kv.Value}"
                };
                Game.Fonts.DrawText(line, 16, y, 18, Color.RayWhite); y += 20;
            }
        }
        // Pact effects panel directly under Mods
        y += 6;
        Game.PactRuntime.DrawHudPanel(16, y, 360);
    }

    private static bool TryHasRed()
    {
        return Assets.TryGet("ui_bar_fg_red.png", out _);
    }

    private static void DrawGameOver()
    {
        var w = Raylib.GetScreenWidth();
        var h = Raylib.GetScreenHeight();
        Raylib.DrawRectangle(0, 0, w, h, new Color(0,0,0,160));
        var text = "GAME OVER - Press R to Restart";
        int size = 32;
        int tw = Raylib.MeasureText(text, size);
        Raylib.DrawText(text, (w - tw)/2, h/2 - size, size, Color.Gold);
    }

    private static void ResetGame(ref Game.Player player, Game.EnemySpawner spawner, ref Game.ProjectilePool proj, ref Game.XPOrbPool xpPool, ref Game.XPSystem xp, ref bool draftOpen, ref bool gameOver, ref float elapsed, ref int score)
    {
        player = new Game.Player(new Vector2(0,0));
        typeof(Game.EnemySpawner).GetField("enemies", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(spawner, new System.Collections.Generic.List<Game.Enemy>());
        proj = new Game.ProjectilePool(512);
        xpPool = new Game.XPOrbPool(512);
        xp = new Game.XPSystem();
        draftOpen = false;
        gameOver = false;
        elapsed = 0f;
        score = 0;
        Game.WeaponPickupSystem.Reset();
    }

    private static void DrawBar(int x, int y, int width, int height, float normalized, string barBg = "ui_bar_bg.png", string barFg = "ui_bar_fg.png")
    {
        if (Assets.TryGet(barBg, out var bg) && Assets.TryGet(barFg, out var fg))
        {
            // Background
            var srcBg = new Rectangle(0, 0, bg.Width, bg.Height);
            var dstBg = new Rectangle(x, y, width, height);
            Raylib.DrawTexturePro(bg, srcBg, dstBg, new Vector2(0, 0), 0, Color.White);

            // Foreground fill portion
            int fillSrc = (int)(fg.Width * normalized);
            if (fillSrc > 0)
            {
                var srcFg = new Rectangle(0, 0, fillSrc, fg.Height);
                var dstFg = new Rectangle(x, y, width * normalized, height);
                Raylib.DrawTexturePro(fg, srcFg, dstFg, new Vector2(0, 0), 0, Color.White);
            }
        }
        else
        {
            // Fallback rectangles
            Raylib.DrawRectangle(x, y, width, height, Color.DarkGray);
            Raylib.DrawRectangle(x, y, (int)(width * normalized), height, Color.Gold);
            Raylib.DrawRectangleLines(x, y, width, height, Color.Black);
        }
    }
}
}

namespace EchoesGame.Game
{
    internal sealed class OrbitalsSystem
    {
        private bool unlocked;
        private float angle;
        private float radius = 60f; // дальше от персонажа
        private float speed = 2.0f; // radians/sec
        private float damage = 8f;
        private Vector2 p1, p2;
        public void Unlock(){ unlocked = true; }
        public bool IsUnlocked => unlocked;
        public void Update(float dt, Vector2 playerPos, IReadOnlyList<Enemy> enemies)
        {
            if (!unlocked) return;
            angle += speed * dt;
            p1 = playerPos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            p2 = playerPos + new Vector2(MathF.Cos(angle + MathF.PI), MathF.Sin(angle + MathF.PI)) * radius;
            // Hit detection (simple radius check)
            Rectangle b1 = new Rectangle(p1.X-6, p1.Y-6, 12, 12);
            Rectangle b2 = new Rectangle(p2.X-6, p2.Y-6, 12, 12);
            for (int i=0;i<enemies.Count;i++)
            {
                var e = enemies[i]; if (!e.Alive) continue;
                if (Raylib.CheckCollisionRecs(b1, e.GetBounds()) || Raylib.CheckCollisionRecs(b2, e.GetBounds()))
                {
                    e.TakeDamage(damage);
                }
            }
        }
        public void Draw()
        {
            if (!unlocked) return;
            Raylib.DrawCircleV(p1, 6, new Color(180, 220, 255, 255));
            Raylib.DrawCircleV(p2, 6, new Color(180, 220, 255, 255));
        }
    }

    internal static class WeaponPickupSystem
    {
        private static bool orbitalDropped = false;
        private static Rectangle? pickup;
        public static void EnsureBossDrop(Vector2 bossPos)
        {
            if (orbitalDropped) return;
            pickup = new Rectangle(bossPos.X, bossPos.Y, 18, 18);
            orbitalDropped = true;
        }
        public static void Update(float dt, ref Player player, OrbitalsSystem orbitals)
        {
            if (pickup.HasValue)
            {
                var r = pickup.Value;
                var pr = new Rectangle(player.Position.X-10, player.Position.Y-10, 20, 20);
                if (Raylib.CheckCollisionRecs(r, pr))
                {
                    orbitals.Unlock();
                    FloatingTextSystem.Spawn(player.Position, "Orbitals unlocked", Color.SkyBlue);
                    pickup = null;
                }
            }
        }
        public static void DrawStatic()
        {
            if (pickup.HasValue)
            {
                var r = pickup.Value;
                Raylib.DrawRectangleRec(r, new Color(80,160,255,200));
                Raylib.DrawRectangleLinesEx(r, 2, Color.White);
            }
        }
        public static void Reset(){ orbitalDropped=false; pickup=null; }
    }
    internal enum KeystoneType { GravityWell, Darkness }

    internal static class KeystoneRuntime
    {
        private static KeystoneType active;
        private static float remaining;
        private static bool activeFlag;
        public static bool IsActiveDarkness => activeFlag && active == KeystoneType.Darkness;
        public static bool IsActiveGravityWell => activeFlag && active == KeystoneType.GravityWell;
        private static Vector2 gravityAnchor = Vector2.Zero;
        private static bool gravityToBoss = false;
        public static void Activate(KeystoneType type, float duration)
        {
            active = type; remaining = duration; activeFlag = true; gravityToBoss = false; gravityAnchor = Vector2.Zero;
        }
        public static void SetGravityAnchor(Vector2 pos)
        { gravityAnchor = pos; gravityToBoss = true; }
        public static void Update(float dt)
        {
            if (!activeFlag) return;
            remaining -= dt; if (remaining <= 0f) { activeFlag = false; remaining = 0f; }
        }
        public static void DrawOverlay()
        {
            if (!activeFlag) return;
            if (active == KeystoneType.Darkness)
            {
                Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), new Color(0,0,0,100));
            }
            string txt = $"Keystone: {active} {remaining:0.0}s";
            Fonts.DrawText(txt, 16, Raylib.GetScreenHeight() - 28, 20, Color.Gold);
        }
        public static void DrawOverlayWorld()
        {
            if (!(activeFlag && active == KeystoneType.GravityWell && gravityToBoss)) return;
            // subtle ring around the boss anchor
            Raylib.DrawCircleLines((int)gravityAnchor.X, (int)gravityAnchor.Y, 60f, new Color(255, 200, 0, 160));
            Raylib.DrawCircleLines((int)gravityAnchor.X, (int)gravityAnchor.Y, 120f, new Color(255, 160, 0, 110));
        }
        public static Vector2 GetGravityForce(Vector2 pos)
        {
            if (!(activeFlag && active == KeystoneType.GravityWell)) return Vector2.Zero;
            Vector2 target = gravityToBoss ? gravityAnchor : Vector2.Zero;
            Vector2 toCenter = target - pos;
            float dist = MathF.Max(1f, toCenter.Length());
            Vector2 dir = toCenter / dist;
            // Stronger, longer falloff: 1/dist with additive bands
            float strength = 700f * 1.28f * (1f / dist);
            if (dist < 700f) strength += 110f * 1.28f;
            if (dist < 350f) strength += 100f * 1.28f;
            strength = MathF.Min(700f * 1.28f, strength);
            return dir * strength;
        }
    }

    internal sealed class EnemyProjectile
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public bool Active;
        private float lifetime = 6.0f;
        public float Damage = 12f;
        public void Reset(Vector2 p, Vector2 v, float dmg) { Position=p; Velocity=v; Damage=dmg; lifetime=3f; Active=true; }
        public void Update(float dt, ref Player player)
        {
            if (!Active) return;
            Position += Velocity * dt;
            lifetime -= dt;
            if (lifetime <= 0f) Active = false;
            if (Raylib.CheckCollisionRecs(new Rectangle(player.Position.X-10, player.Position.Y-10,20,20), new Rectangle(Position.X-6, Position.Y-6,12,12)))
            { player.TakeDamage(Damage); Active=false; }
        }
        public void Draw()
        {
            if (!Active) return; Raylib.DrawCircleV(Position, 5f, Color.Orange);
        }
    }

    internal sealed class EnemyProjectilePool
    {
        private readonly EnemyProjectile[] pool;
        private int next;
        public EnemyProjectilePool(int capacity) { pool = new EnemyProjectile[capacity]; for(int i=0;i<capacity;i++) pool[i]=new EnemyProjectile(); }
        public void Spawn(Vector2 p, Vector2 v, float dmg) { for(int i=0;i<pool.Length;i++){ next=(next+1)%pool.Length; if(!pool[next].Active){ pool[next].Reset(p,v,dmg); return; } } }
        public void Update(float dt, ref Player player){ for(int i=0;i<pool.Length;i++) pool[i].Update(dt, ref player); }
        public void Draw(){ for(int i=0;i<pool.Length;i++) pool[i].Draw(); }
    }

    internal sealed class BossManager
    {
        public bool Alive => alive;
        private bool alive;
        public Vector2 Position;
        public Rectangle Bounds => new Rectangle(Position.X - 32, Position.Y - 32, 64, 64);
        private float hpMax = 800f;
        private float hp;
        private float shootTimer;
        private const float ShootInterval = 0.7f; // fan burst
        // Telegraph state
        private bool telegraphActive;
        private float telegraphTimer;
        private const float TelegraphDuration = 0.45f;
        private float telegraphBaseAngleRad;
        private float fireWatchdogTimer;
        public Action<Vector2>? OnDeath;
        public void SpawnNear(Camera2D camera)
        {
            float left = camera.Target.X - camera.Offset.X;
            float top = camera.Target.Y - camera.Offset.Y;
            Position = new Vector2(left + camera.Offset.X, top + camera.Offset.Y - 120);
            hp = hpMax; alive = true;
            shootTimer = 0f;
            telegraphActive = false; telegraphTimer = 0f; telegraphBaseAngleRad = 0f;
            fireWatchdogTimer = 0f;
        }
        public void Update(float dt, Vector2 playerPos)
        {
            if (!alive) return;
            // gravity well affects player via KeystoneRuntime; boss chases slowly
            Vector2 dir = playerPos - Position;
            if (dir.LengthSquared() > 1e-4f) dir = Vector2.Normalize(dir);
            Position += dir * 80f * dt;
            // Face toward player for rendering
            float angleDeg = MathF.Atan2(dir.Y, dir.X) * (180f / MathF.PI);
            facingDeg = angleDeg;
        }
        public void Update(float dt, Vector2 playerPos, EnemyProjectilePool shots)
        {
            if (!alive) return; Update(dt, playerPos);
            shootTimer += dt;
            fireWatchdogTimer += dt;
            if (!telegraphActive && shootTimer >= ShootInterval)
            {
                // start telegraph
                telegraphActive = true; telegraphTimer = 0f;
                Vector2 to = playerPos - Position; if (to.LengthSquared() < 1e-4f) to = new Vector2(1, 0); to = Vector2.Normalize(to);
                telegraphBaseAngleRad = MathF.Atan2(to.Y, to.X);
                Infra.Analytics.Log("boss_fanshot_telegraph_start", new System.Collections.Generic.Dictionary<string, object>{{"t", Raylib.GetTime()}});
            }
            if (telegraphActive)
            {
                telegraphTimer += dt;
                if (telegraphTimer >= TelegraphDuration)
                {
                    // fire after telegraph, reset cycle
                    telegraphActive = false; shootTimer = 0f;
                    for (int i = -2; i <= 2; i++)
                    {
                        float ang = telegraphBaseAngleRad + i * 0.11f;
                        Vector2 v = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * 320f;
                        shots.Spawn(Position, v, 8f);
                    }
                    Infra.Analytics.Log("boss_fanshot_fire", new System.Collections.Generic.Dictionary<string, object>{{"t", Raylib.GetTime()}});
                    fireWatchdogTimer = 0f;
                }
            }
            // Watchdog: if по какой-то причине ни телеграфа, ни выстрела долго нет — принудительный залп
            if (!telegraphActive && fireWatchdogTimer > ShootInterval * 2.5f)
            {
                Vector2 to = playerPos - Position; if (to.LengthSquared() < 1e-4f) to = new Vector2(1, 0); to = Vector2.Normalize(to);
                float baseAng = MathF.Atan2(to.Y, to.X);
                for (int i = -2; i <= 2; i++)
                {
                    float ang = baseAng + i * 0.11f;
                    Vector2 v = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * 320f;
                    shots.Spawn(Position, v, 8f);
                }
                fireWatchdogTimer = 0f; shootTimer = 0f;
            }
        }
        private float facingDeg;
        public void Draw()
        {
            if (!alive) return;
            // Use tank texture scaled x2 and tinted yellow
            if (Assets.TryGet("enemy_tank.png", out var tex))
            {
                var src = new Rectangle(0, 0, tex.Width, tex.Height);
                var dst = new Rectangle(Position.X, Position.Y, tex.Width * 2f, tex.Height * 2f);
                var origin = new Vector2(tex.Width, tex.Height);
                Raylib.DrawTexturePro(tex, src, dst, origin, facingDeg, new Color(255, 255, 0, 230));
            }
            else
            {
                Raylib.DrawCircleV(Position, 28f, new Color(255,255,0,180));
                Raylib.DrawCircleLines((int)Position.X, (int)Position.Y, 32f, Color.Gold);
            }
            // Telegraph wedge overlay
            if (telegraphActive)
            {
                float baseDeg = telegraphBaseAngleRad * (180f / MathF.PI);
                float stepDeg = 0.11f * (180f / MathF.PI);
                float start = baseDeg - stepDeg * 2f - 4f;
                float end = baseDeg + stepDeg * 2f + 4f;
                Raylib.DrawCircleSector(Position, 220f, start, end, 28, new Color(255, 200, 0, 80));
                Raylib.DrawCircleSectorLines(Position, 220f, start, end, 28, new Color(255, 220, 0, 180));
            }
        }
        public void DrawBossHud()
        {
            if (!alive) return;
            int w = 300; int h = 14; int x = (Raylib.GetScreenWidth()-w)/2; int y = 16;
            Raylib.DrawRectangle(x, y, w, h, Color.DarkGray);
            float n = MathF.Max(0f, hp) / hpMax; Raylib.DrawRectangle(x, y, (int)(w*n), h, Color.Red);
            Fonts.DrawText("MINI-BOSS", x, y-18, 20, Color.Gold);
        }
        public void TakeDamage(float d)
        {
            if (!alive) return; hp -= d; if (hp <= 0f) { alive = false; FloatingTextSystem.Spawn(Position, "+XP", Color.Gold); OnDeath?.Invoke(Position); }
        }
    }
    internal static class PactsMenuOverlay
    {
        public static void Draw()
        {
            // Dim background + panel centered
            int w = Raylib.GetScreenWidth();
            int h = Raylib.GetScreenHeight();
            Raylib.DrawRectangle(0, 0, w, h, new Color(0,0,0,160));
            int panelW = Math.Min(900, (int)(w * 0.8));
            int panelH = Math.Min(540, (int)(h * 0.8));
            int x = (w - panelW)/2; int y = (h - panelH)/2;
            Raylib.DrawRectangle(x, y, panelW, panelH, new Color(40,40,40,220));
            Raylib.DrawRectangleLines(x, y, panelW, panelH, Color.DarkGray);

            Fonts.DrawText("Pacts Overview (O to close)", x + 16, y + 12, 26, Color.Gold);
            int cy = y + 52;
            int col = 2;
            int gap = 16;
            int cardW = (panelW - 16*2 - gap) / col;
            int cardH = 100;
            var catalog = PactRuntime.GetPermanentCatalog();
            for (int i = 0; i < catalog.Count; i++)
            {
                int row = i / col;
                int c = i % col;
                int cx = x + 16 + c * (cardW + gap);
                int cyRow = cy + row * (cardH + gap);
                var r = new Rectangle(cx, cyRow, cardW, cardH);
                bool owned = PactRuntime.HasPermanent(catalog[i]);
                int stacks = PactRuntime.GetPermanentStacks(catalog[i]);
                var bg = owned ? new Color(80,80,80,240) : new Color(60,60,60,160);
                Raylib.DrawRectangleRec(r, bg);
                Raylib.DrawRectangleLinesEx(r, 2, owned ? Color.Gold : Color.DarkGray);
                string label = owned && stacks > 1 ? $"{catalog[i]} x{stacks}" : catalog[i];
                // Wrapped title
                DrawTextWrapped(label, r, 20, 4, owned ? Color.RayWhite : new Color(180,180,180,220));
            }
            // Timed effects row
            int timedY = cy + ((catalog.Count + (col-1))/col) * (cardH + gap) + 8;
            Fonts.DrawText("Active timed effects:", x + 16, timedY, 22, Color.Gold);
            var actives = PactRuntime.GetActiveTimed();
            int ty = timedY + 24;
            for (int i = 0; i < actives.Count; i++)
            {
                var a = actives[i];
                Fonts.DrawText($"- {a.Name} {a.Strength*100:0}% — {a.Remaining:0.0}s", x + 28, ty, 20, Color.RayWhite);
                ty += 20;
            }
        }

        private static void DrawTextWrapped(string text, Rectangle bounds, int fontSize, int lineSpacing, Color color)
        {
            int margin = 8; int maxW = (int)bounds.Width - margin*2; int x = (int)bounds.X + margin; int y = (int)bounds.Y + margin;
            string line = string.Empty; var words = text.Split(' ');
            for (int wi = 0; wi < words.Length; wi++)
            {
                string test = string.IsNullOrEmpty(line) ? words[wi] : line + " " + words[wi];
                int width = Raylib.MeasureText(test, fontSize);
                if (width > maxW)
                {
                    Fonts.DrawText(line, x, y, fontSize, color); y += fontSize + lineSpacing; line = words[wi];
                }
                else line = test;
            }
            if (!string.IsNullOrEmpty(line)) Fonts.DrawText(line, x, y, fontSize, color);
        }
    }
    internal static class Fonts
    {
        private static Font? uiFont;
        public static void Init(string fontPath)
        {
            if (System.IO.File.Exists(fontPath))
            {
                uiFont = Raylib.LoadFontEx(fontPath, 32, null, 0);
                // No direct texture field access; DrawTextEx will handle filtering adequately
            }
        }
        public static void DrawText(string text, int x, int y, int size, Color color)
        {
            if (uiFont.HasValue)
            {
                Raylib.DrawTextEx(uiFont.Value, text, new Vector2(x, y), size, 1, color);
            }
            else
            {
                Raylib.DrawText(text, x, y, size, color);
            }
        }
    }
    internal struct FloatingText
    {
        public Vector2 Position;
        public string Text;
        public Color Color;
        public float Lifetime;
        public float Age;
    }

    internal static class FloatingTextSystem
    {
        private static readonly List<FloatingText> items = new();
        public static void Spawn(Vector2 pos, string text, Color color)
        {
            Vector2 p = pos;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                float d2 = Vector2.DistanceSquared(it.Position, p);
                if (d2 < 18f * 18f)
                {
                    p.Y -= 18f;
                }
            }
            items.Add(new FloatingText { Position = p, Text = text, Color = color, Lifetime = 1.1f, Age = 0f });
        }
        public static void Update(float dt)
        {
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var it = items[i];
                it.Age += dt;
                it.Position.Y -= 48f * dt; // float up faster
                items[i] = it;
                if (it.Age >= it.Lifetime) items.RemoveAt(i);
            }
        }
        public static void Draw()
        {
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                float a = MathF.Max(0f, 1f - it.Age / it.Lifetime);
                var baseC = it.Color;
                var c = new Color(baseC.R, baseC.G, baseC.B, (byte)(255 * a));
                Game.Fonts.DrawText(it.Text, (int)it.Position.X, (int)it.Position.Y, 24, c);
            }
        }
        public static void Clear() { items.Clear(); }
    }
    internal enum PactEffect { XPGain, EliteChance }

    internal static class PactRuntime
    {
        public struct TimedView { public string Name; public float Strength; public float Remaining; }
        private struct ActiveEffect { public PactEffect Type; public float Remaining; public float Strength; }
        private static readonly List<ActiveEffect> active = new();
        private static float xpGainBonus = 0f;
        private static float eliteChanceBonus = 0f;
        private static readonly List<string> permanent = new();
        private static readonly Dictionary<string, int> permanentStacks = new();
        private static readonly string[] allPermanentCatalog = new[] {
            "Storm Pact: +20% fire rate, +15% enemy speed",
            "Stone Pact: Heal to full, +20% Max HP, -15% move speed",
            "Blood Oath: +25% bullet damage, -15% Max HP",
            "Time Bargain: -20% dash cooldown, +10% enemy speed"
        };

        public static float XPGainBonus => xpGainBonus; // 0.40 = +40%
        public static float EliteChanceBonus => eliteChanceBonus; // 0.20 = +20%

        public static void ApplyTimed(PactEffect type, float duration, float strength)
        {
            active.Add(new ActiveEffect { Type = type, Remaining = duration, Strength = strength });
            RecomputeTotals();
        }

        public static void AddPermanent(string description)
        {
            permanent.Add(description);
            if (!permanentStacks.ContainsKey(description)) permanentStacks[description] = 0;
            permanentStacks[description] += 1;
        }

        public static void Update(float dt)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var e = active[i];
                e.Remaining -= dt;
                if (e.Remaining <= 0f) active.RemoveAt(i); else active[i] = e;
            }
            RecomputeTotals();
        }

        private static void RecomputeTotals()
        {
            xpGainBonus = 0f; eliteChanceBonus = 0f;
            foreach (var e in active)
            {
                if (e.Type == PactEffect.XPGain) xpGainBonus += e.Strength;
                else if (e.Type == PactEffect.EliteChance) eliteChanceBonus += e.Strength;
            }
        }

        public static void DrawHudPanel(int x, int y, int width)
        {
            if (active.Count == 0) return;
            int padding = 8;
            int rowH = 22;
            int contentW = width - 4; // want full bar width inside outer border
            int height = padding * 2 + 20 + active.Count * rowH;
            // Full opaque gray backdrop, then black translucent underlay for the bar area
            Raylib.DrawRectangle(x, y, width, height, new Color(80, 80, 80, 255));
            Raylib.DrawRectangleLines(x, y, width, height, Color.Maroon);
            // Title with slight outline
            Fonts.DrawText("Pact effects (timed):", x + padding + 1, y + padding + 1, 20, Color.Black);
            Fonts.DrawText("Pact effects (timed):", x + padding, y + padding, 20, Color.Gold);
            int line = 20;
            for (int i = 0; i < active.Count; i++)
            {
                var e = active[i];
                string name = e.Type switch { PactEffect.XPGain => "Greed Pact: +XP", PactEffect.EliteChance => "Greed Pact: +Elite chance", _ => "Effect" };
                string txt = $"{name} {e.Strength*100:0}% — {e.Remaining:0.0}s";
                // progress fill: red shrinking bar spans entire inner width; background under it is semi‑transparent black
                float total = GetEffectTotalDuration(e.Type);
                float frac = total <= 0f ? 0f : (e.Remaining / total);
                if (frac < 0f) frac = 0f; if (frac > 1f) frac = 1f;
                int rowTop = y + padding + line - 2;
                int left = x + 2; // inside border
                // background (empty) area
                Raylib.DrawRectangle(left, rowTop, contentW, rowH - 2, new Color(0, 0, 0, 120));
                // red filled remaining time
                Raylib.DrawRectangle(left, rowTop, (int)(contentW * frac), rowH - 2, new Color(160, 0, 0, 180));
                // title with outline on the bar
                Game.Fonts.DrawText(txt, x + padding + 3, rowTop + 3, 18, Color.Black);
                Game.Fonts.DrawText(txt, x + padding + 2, rowTop + 2, 18, Color.Yellow);
                line += rowH;
            }
        }

        private static float GetEffectTotalDuration(PactEffect type)
        {
            return type switch { PactEffect.XPGain => 30f, PactEffect.EliteChance => 30f, _ => 30f };
        }

        public static void DrawPermanentPanel(int x, int y, int width)
        {
            int padding = 8;
            int lines = Math.Max(1, allPermanentCatalog.Length);
            int height = padding * 2 + 20 + lines * 18;
            Raylib.DrawRectangle(x, y, width, height, new Color(60, 60, 60, 140));
            Raylib.DrawRectangleLines(x, y, width, height, Color.DarkGray);
            Fonts.DrawText("Permanent Pacts:", x + padding, y + padding, 20, Color.Gold);
            int line = 20;
            for (int i = 0; i < allPermanentCatalog.Length; i++)
            {
                string text = allPermanentCatalog[i];
                bool owned = permanentStacks.ContainsKey(text);
                int stacks = owned ? permanentStacks[text] : 0;
                string label = owned && stacks > 1 ? $"{text} x{stacks}" : text;
                var color = owned ? Color.RayWhite : new Color(180,180,180,200);
                Fonts.DrawText(label, x + padding, y + padding + line, 18, color);
                line += 18;
            }
        }

        public static List<string> GetPermanentCatalog() => allPermanentCatalog.ToList();
        public static bool HasPermanent(string label) => permanentStacks.ContainsKey(label);
        public static int GetPermanentStacks(string label) => permanentStacks.TryGetValue(label, out var s) ? s : 0;
        public static List<TimedView> GetActiveTimed()
        {
            var list = new List<TimedView>();
            foreach (var e in active)
            {
                string name = e.Type switch { PactEffect.XPGain => "Greed Pact: +XP", PactEffect.EliteChance => "Greed Pact: +Elite chance", _ => "Effect" };
                list.Add(new TimedView { Name = name, Strength = e.Strength, Remaining = e.Remaining });
            }
            return list;
        }
    }
    

    internal sealed class Player
    {
        public Vector2 Position { get; private set; }
        private Vector2 velocity;
        private const float MoveSpeed = 300f;
        private const float DashSpeed = 1100f;
        private const float DashDuration = 0.20f; // seconds of iFrames
        private const float DashCooldown = 4.0f;
        public float BulletSpeedMultiplier = 1.0f;
        public float ShootIntervalMultiplier = 1.0f;
        public float BulletDamageMultiplier = 1.0f;
        public float PlayerSpeedMultiplier = 1.0f;
        public float DashCooldownMultiplier = 1.0f;
        public float XPMagnetMultiplier = 1.0f;
        public readonly Dictionary<ModType, int> ModStacks = new();

        public enum ModType { BulletSpeed, ShootInterval, BulletDamage, MoveSpeed, XPMagnet, DashCooldown }

        public void ApplyMod(ModType type, int stacks = 1)
        {
            if (!ModStacks.ContainsKey(type)) ModStacks[type] = 0;
            ModStacks[type] += stacks;
            RecomputeMultipliers();
        }

        private int GetStacks(ModType t) => ModStacks.TryGetValue(t, out var v) ? v : 0;

        private void RecomputeMultipliers()
        {
            BulletSpeedMultiplier = 1f + 0.15f * GetStacks(ModType.BulletSpeed);
            BulletDamageMultiplier = 1f + 0.20f * GetStacks(ModType.BulletDamage);
            ShootIntervalMultiplier = MathF.Max(0.3f, 1f - 0.10f * GetStacks(ModType.ShootInterval));
            PlayerSpeedMultiplier = 1f + 0.10f * GetStacks(ModType.MoveSpeed);
            XPMagnetMultiplier = 1f + 0.20f * GetStacks(ModType.XPMagnet);
            DashCooldownMultiplier = MathF.Max(0.3f, 1f - 0.10f * GetStacks(ModType.DashCooldown));
        }

        // Health
        public float MaxHP { get; private set; } = 100f;
        public float HP { get; private set; } = 100f;
        public bool IsDead => HP <= 0f;
        private float damageIFrames = 0f;
        public void TakeDamage(float dmg)
        {
            if (damageIFrames > 0f) return;
            HP -= dmg;
            if (HP < 0f) HP = 0f;
            damageIFrames = 0.4f; // minimal tick between damage
            // Player hit feedback
            Game.FloatingTextSystem.Spawn(Position, $"{dmg:0}", Color.Red);
            LastDamageCause = "hit";
        }
        public string LastDamageCause { get; private set; } = "unknown";
        public void ApplyExternalForce(Vector2 force, float scale)
        {
            Position += force * scale;
            float halfW = Config.WorldWidth / 2f - 16f;
            float halfH = Config.WorldHeight / 2f - 16f;
            Position = new Vector2(Config.Clamp(Position.X, -halfW, halfW), Config.Clamp(Position.Y, -halfH, halfH));
        }
        public void TickDamageIFrames(float dt) { if (damageIFrames > 0f) damageIFrames -= dt; }
        public void HealPercent(float p)
        {
            float heal = MaxHP * p;
            HP = MathF.Min(MaxHP, HP + heal);
            if (heal > 0f) Game.FloatingTextSystem.Spawn(Position, $"+{heal:0}", Color.Green);
        }

        public void IncreaseMaxHPByPercent(float percent, bool healToFull)
        {
            float mult = 1f + percent;
            MaxHP *= mult;
            if (healToFull) HP = MaxHP; else HP = MathF.Min(MaxHP, HP);
        }

        private bool isDashing;
        private float dashTimer;
        private float dashCooldownTimer;
        private Vector2 dashDirection;

        public float DashCooldownRemaining => MathF.Max(0f, DashCooldown - dashCooldownTimer);
        public float DashCooldownTotal => DashCooldown;

        public Player(Vector2 start)
        {
            Position = start;
        }

        public void Update(float dt, ProjectilePool projectiles, Camera2D camera)
        {
            UpdateMovement(dt);
            HandleShooting(dt, projectiles, camera);
        }

        private void UpdateMovement(float dt)
        {
            if (!isDashing)
            {
                Vector2 input = Vector2.Zero;
                if (Raylib.IsKeyDown(KeyboardKey.W)) input.Y -= 1f;
                if (Raylib.IsKeyDown(KeyboardKey.S)) input.Y += 1f;
                if (Raylib.IsKeyDown(KeyboardKey.A)) input.X -= 1f;
                if (Raylib.IsKeyDown(KeyboardKey.D)) input.X += 1f;
                if (input.LengthSquared() > 1e-5f) input = Vector2.Normalize(input);
                velocity = input * (MoveSpeed * PlayerSpeedMultiplier);
                Position += velocity * dt;

                // Clamp to world bounds
                float halfW = Config.WorldWidth / 2f - 16f;
                float halfH = Config.WorldHeight / 2f - 16f;
                Position = new Vector2(Config.Clamp(Position.X, -halfW, halfW), Config.Clamp(Position.Y, -halfH, halfH));

                // Dash start
                if (dashCooldownTimer >= DashCooldown * DashCooldownMultiplier && Raylib.IsKeyPressed(KeyboardKey.LeftShift))
                {
                    isDashing = true;
                    dashTimer = 0f;
                    dashDirection = input.LengthSquared() < 1e-5f ? new Vector2(1, 0) : Vector2.Normalize(input);
                    Infra.Analytics.Log("dash", new System.Collections.Generic.Dictionary<string, object>{{"t", Raylib.GetTime()},{"x", Position.X},{"y", Position.Y}});
                }

                dashCooldownTimer += dt;
            }
            else
            {
                dashTimer += dt;
                Position += dashDirection * DashSpeed * dt;
                if (dashTimer >= DashDuration)
                {
                    isDashing = false;
                    dashCooldownTimer = 0f;
                }
                float halfW2 = Config.WorldWidth / 2f - 16f;
                float halfH2 = Config.WorldHeight / 2f - 16f;
                Position = new Vector2(Config.Clamp(Position.X, -halfW2, halfW2), Config.Clamp(Position.Y, -halfH2, halfH2));
            }
        }

        private float shootTimer;
        private const float ShootIntervalBase = 0.12f; // auto-fire base

        private void HandleShooting(float dt, ProjectilePool projectiles, Camera2D camera)
        {
            shootTimer += dt;
            float shootInterval = ShootIntervalBase * ShootIntervalMultiplier;
            if (shootTimer >= shootInterval)
            {
                shootTimer = 0f;
                // Direction towards mouse in world space
                Vector2 mouseScreen = Raylib.GetMousePosition();
                Vector2 mouseWorld = Raylib.GetScreenToWorld2D(mouseScreen, camera);
                Vector2 dir = mouseWorld - Position;
                if (dir.LengthSquared() < 1e-5f) dir = new Vector2(1, 0);
                dir = Vector2.Normalize(dir);
                float speed = 900f * BulletSpeedMultiplier;
                float dmg = 10f * BulletDamageMultiplier;
                projectiles.Spawn(Position, dir * speed, dmg);
            }
        }

        public void Draw(Camera2D camera)
        {
            // Body rotated towards mouse like weapon
            if (Assets.TryGet("player_human_idle.png", out var body))
            {
                Vector2 mouse = Raylib.GetMousePosition();
                Vector2 mouseWorld = Raylib.GetScreenToWorld2D(mouse, camera);
                Vector2 dir = mouseWorld - Position;
                float angleDeg = MathF.Atan2(dir.Y, dir.X) * (180f / MathF.PI);
                var src = new Rectangle(0, 0, body.Width, body.Height);
                var dst = new Rectangle(Position.X, Position.Y, body.Width, body.Height);
                var origin = new Vector2(body.Width / 2f, body.Height / 2f);
                var tint = damageIFrames > 0.2f ? new Color(255, 120, 120, 255) : Color.White; // brief red tint on recent hit
                Raylib.DrawTexturePro(body, src, dst, origin, angleDeg, tint);
            }
            else
            {
                Color color = isDashing ? Color.Gold : Color.SkyBlue;
                Raylib.DrawCircleV(Position, 14f, color);
            }

            // Weapon (rotates towards mouse, anchored at player center)
            if (Assets.TryGet("weapon_basic.png", out var gun))
            {
                Vector2 mouse = Raylib.GetMousePosition();
                Vector2 mouseWorld = Raylib.GetScreenToWorld2D(mouse, camera);
                Vector2 dir = mouseWorld - Position;
                float angleDeg = MathF.Atan2(dir.Y, dir.X) * (180f / MathF.PI);
                var src = new Rectangle(0, 0, gun.Width, gun.Height);
                var dst = new Rectangle(Position.X, Position.Y, gun.Width, gun.Height);
                var origin = new Vector2(gun.Width / 2f, gun.Height / 2f);
                Raylib.DrawTexturePro(gun, src, dst, origin, angleDeg, Color.White);
            }
        }
    }

    internal sealed class Projectile
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public bool Active;
        private const float Radius = 4f;
        private float lifetime = 2.0f;
        public float Damage = 10f;

        public void Reset(Vector2 pos, Vector2 vel, float damage)
        {
            Position = pos;
            Velocity = vel;
            Damage = damage;
            lifetime = 2.0f;
            Active = true;
        }

        public void Update(float dt)
        {
            if (!Active) return;
            Position += Velocity * dt;
            lifetime -= dt;
            if (Position.X < 32 || Position.X > 1280 - 32 || Position.Y < 32 || Position.Y > 720 - 32)
            {
                // No hard world bounds; keep bullet alive
            }
            if (lifetime <= 0f) Active = false;
        }

        public void Draw()
        {
            if (!Active) return;
            if (Assets.TryGet("bullet_basic.png", out var tex))
            {
                float angleDeg = MathF.Atan2(Velocity.Y, Velocity.X) * (180f / MathF.PI);
                var src = new Rectangle(0, 0, tex.Width, tex.Height);
                var dst = new Rectangle(Position.X, Position.Y, tex.Width, tex.Height);
                var origin = new Vector2(tex.Width / 2f, tex.Height / 2f);
                Raylib.DrawTexturePro(tex, src, dst, origin, angleDeg, Color.White);
            }
            else
            {
                Raylib.DrawCircleV(Position, Radius, Color.Lime);
            }
        }

        public Rectangle GetBounds() => new Rectangle(Position.X - Radius, Position.Y - Radius, Radius * 2, Radius * 2);
    }

    internal sealed class ProjectilePool
    {
        private readonly Projectile[] pool;
        private int next;

        public ProjectilePool(int capacity)
        {
            pool = new Projectile[capacity];
            for (int i = 0; i < capacity; i++) pool[i] = new Projectile();
        }

        public void Spawn(Vector2 pos, Vector2 vel, float damage = 10f)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                next = (next + 1) % pool.Length;
                if (!pool[next].Active)
                {
                    pool[next].Reset(pos, vel, damage);
                    return;
                }
            }
        }

        public void Update(float dt)
        {
            for (int i = 0; i < pool.Length; i++) pool[i].Update(dt);
        }

        public void Draw()
        {
            for (int i = 0; i < pool.Length; i++) pool[i].Draw();
        }

        public IEnumerable<Projectile> All() => pool;

        public int ActiveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < pool.Length; i++)
                {
                    if (pool[i].Active) count++;
                }
                return count;
            }
        }
    }

    internal sealed class Enemy
    {
        public Vector2 Position;
        public float Speed = 110f;
        private const float Radius = 12f;
        public bool Alive = true;
        public float MaxHP = 20f;
        public float HP = 20f;
        public bool IsTank = false;
        public bool IsSprinter = false;
        public bool IsElite = false;
        public EnemyMod Modifier = EnemyMod.None;
        private float hitFlash;
        public float ContactDamage => IsTank ? 25f : 15f;
        private float faceAngleDeg;

        public void Update(float dt, Vector2 target)
        {
            if (!Alive) return;
            Vector2 dir = target - Position;
            if (dir.LengthSquared() > 1e-5f)
            {
                faceAngleDeg = MathF.Atan2(dir.Y, dir.X) * (180f / MathF.PI);
            }
            if (dir.LengthSquared() > 1e-5f)
            {
                dir = Vector2.Normalize(dir);
                Position += dir * Speed * dt;
            }
            if (hitFlash > 0f) hitFlash -= dt;
        }

        public void Draw()
        {
            if (!Alive) return;
            string texName = IsTank ? "enemy_tank.png" : (IsSprinter ? "enemy_sprinter.png" : "enemy_chaser.png");
            if (Assets.TryGet(texName, out var tex))
            {
                var src = new Rectangle(0, 0, tex.Width, tex.Height);
                float scale = IsElite ? 1.25f : 1f;
                var dst = new Rectangle(Position.X, Position.Y, tex.Width * scale, tex.Height * scale);
                var origin = new Vector2(tex.Width / 2f, tex.Height / 2f);
                var tint = hitFlash > 0f ? Color.Orange : (Modifier == EnemyMod.Berserk ? Color.Red : (IsSprinter ? Color.SkyBlue : Color.White));
                Raylib.DrawTexturePro(tex, src, dst, origin, faceAngleDeg, tint);
                if (IsElite && Modifier == EnemyMod.Shielded)
                {
                    float ringR = MathF.Max(dst.Width, dst.Height) / 2f + 6f;
                    Raylib.DrawCircleLines((int)Position.X, (int)Position.Y, ringR, Color.RayWhite);
                }
                // HP bar
                float w = 32f; float h = 4f;
                float n = MathF.Max(0f, HP) / MathF.Max(1f, MaxHP);
                Raylib.DrawRectangle((int)(Position.X - w / 2f), (int)(Position.Y - tex.Height / 2f - 10), (int)w, (int)h, Color.DarkGray);
                Raylib.DrawRectangle((int)(Position.X - w / 2f), (int)(Position.Y - tex.Height / 2f - 10), (int)(w * n), (int)h, IsTank ? Color.Red : Color.Green);
            }
            else
            {
                Raylib.DrawCircleV(Position, Radius, Color.Maroon);
            }
        }

        public Rectangle GetBounds() => new Rectangle(Position.X - Radius, Position.Y - Radius, Radius * 2, Radius * 2);
        public void TakeDamage(float dmg)
        {
            // Elite shielded reduces incoming damage
            if (IsElite && Modifier == EnemyMod.Shielded) dmg *= 0.6f;
            HP -= dmg;
            hitFlash = 0.1f;
            if (HP <= 0f) Alive = false;
            // Floating text for enemy damage is handled centrally in Collision.Resolve
        }
    }

    internal enum EnemyMod { None, Shielded, Berserk }

    internal sealed class XPSystem
    {
        public int Level { get; private set; } = 1;
        public int CurrentXP { get; private set; }
        public int NextLevelXP { get; private set; } = 5;
        public int PendingChoices { get; private set; }

        public float ProgressNormalized => MathF.Min(1f, (float)CurrentXP / NextLevelXP);

        public void AddXP(int amount)
        {
            int bonus = amount + (int)MathF.Ceiling(amount * PactRuntime.XPGainBonus);
            CurrentXP += Math.Max(amount, bonus);
            while (CurrentXP >= NextLevelXP)
            {
                CurrentXP -= NextLevelXP;
                Level++;
                NextLevelXP = (int)(NextLevelXP * 1.20f + 1);
                PendingChoices++;
                Infra.Analytics.Log("level_up", new System.Collections.Generic.Dictionary<string, object>{{"t", Raylib.GetTime()},{"level", Level}});
            }
        }

        public void ConsumePending()
        {
            if (PendingChoices > 0) PendingChoices--;
        }
    }

    internal sealed class XPOrb
    {
        public Vector2 Position;
        public bool Active;
        public int Amount;
        private float pickupRadius = 24f;

        public void Update(float dt, Vector2 playerPos)
        {
            if (!Active) return;
            float dist = Vector2.Distance(playerPos, Position);
            if (dist < pickupRadius)
            {
                // Will be handled by pool to apply XP
            }
        }

        public void Draw()
        {
            if (!Active) return;
            if (Assets.TryGet("xp_orb.png", out var tex))
            {
                float scale = 0.25f; // downscale 128->32
                var src = new Rectangle(0, 0, tex.Width, tex.Height);
                var dst = new Rectangle(Position.X, Position.Y, tex.Width * scale, tex.Height * scale);
                var origin = new Vector2((tex.Width * scale) / 2f, (tex.Height * scale) / 2f);
                Raylib.DrawTexturePro(tex, src, dst, origin, 0, Color.White);
            }
            else
            {
                Raylib.DrawCircleV(Position, 8f, Color.Green);
            }
        }

        public Rectangle GetBounds(float radius = 12f) => new Rectangle(Position.X - radius, Position.Y - radius, radius * 2, radius * 2);
    }

    internal sealed class LevelUpDraft
    {
        private readonly string[] titles = new[] {
            "+15% bullet speed", "+10% attack speed", "+20% damage",
            "+10% move speed", "+20% XP magnet", "-10% dash cooldown" };
        private readonly string[] descs = new[] {
            "Bullets travel faster", "Shoot faster (higher APS)", "Increase overall damage",
            "Player moves faster", "XP orbs pull from farther", "Dash cooldown reduced" };
        private Rectangle[] cardRects = Array.Empty<Rectangle>();
        private static Font? cachedFont => FontsFontRef;
        private static Font? FontsFontRef;

        public void Open()
        {
            int x = 200; int y = 200; int w = 280; int h = 120; int gap = 24;
            // pick 3 unique indices from the list
            int[] idx = ShufflePick(Enumerable.Range(0, titles.Length).ToArray(), 3);
            cardRects = new Rectangle[] { new Rectangle(x, y, w, h), new Rectangle(x + w + gap, y, w, h), new Rectangle(x + (w + gap)*2, y, w, h) };
            shown = idx;
        }

        public void Draw()
        {
            // Dim background
            Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), new Color(0,0,0,180));
            for (int i = 0; i < cardRects.Length; i++)
            {
                var r = cardRects[i];
                bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
                var bg = hover ? new Color(120, 120, 120, 255) : Color.DarkGray;
                Raylib.DrawRectangleRec(r, bg);
                Raylib.DrawRectangleLinesEx(r, 2, hover ? Color.Gold : Color.Black);
                int k = shown[i];
                Fonts.DrawText($"{i+1}. {titles[k]}", (int)r.X + 12, (int)r.Y + 12, 20, Color.RayWhite);
                Fonts.DrawText(descs[k], (int)r.X + 12, (int)r.Y + 44, 18, Color.RayWhite);
            }
        }

        public int HitTest(Vector2 mouseScreen)
        {
            for (int i = 0; i < cardRects.Length; i++) if (Raylib.CheckCollisionPointRec(mouseScreen, cardRects[i])) return i;
            return -1;
        }

        public void Apply(int index, Player player)
        {
            int k = shown[index];
            switch (k)
            {
                case 0: player.ApplyMod(Player.ModType.BulletSpeed); break;
                case 1: player.ApplyMod(Player.ModType.ShootInterval); break;
                case 2: player.ApplyMod(Player.ModType.BulletDamage); break;
                case 3: player.ApplyMod(Player.ModType.MoveSpeed); break;
                case 4: player.ApplyMod(Player.ModType.XPMagnet); break;
                case 5: player.ApplyMod(Player.ModType.DashCooldown); break;
            }
        }

        private int[] shown = new int[3];

        private static int[] ShufflePick(int[] arr, int count)
        {
            var rnd = new Random();
            for (int i = arr.Length - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
            return arr.Take(count).ToArray();
        }
    }

    internal sealed class XPOrbPool
    {
        private readonly XPOrb[] pool;
        private int next;

        public XPOrbPool(int capacity)
        {
            pool = new XPOrb[capacity];
            for (int i = 0; i < capacity; i++) pool[i] = new XPOrb();
        }

        public void Spawn(Vector2 pos, int amount)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                next = (next + 1) % pool.Length;
                if (!pool[next].Active)
                {
                    pool[next].Active = true;
                    pool[next].Position = pos;
                    pool[next].Amount = amount;
                    return;
                }
            }
        }

        public void Update(float dt, Vector2 playerPos, XPSystem xp, float magnetMultiplier)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                var o = pool[i];
                if (!o.Active) continue;
                // simple magnet
                float dist = Vector2.Distance(playerPos, o.Position);
                float magnetRadius = 160f * magnetMultiplier;
                if (dist < magnetRadius)
                {
                    Vector2 dir = Vector2.Normalize(playerPos - o.Position);
                    float pull = MathF.Max(200f, (600f * magnetMultiplier) - dist * 2f);
                    o.Position += dir * pull * dt;
                }
                if (Raylib.CheckCollisionRecs(o.GetBounds(10f), new Rectangle(playerPos.X - 8, playerPos.Y - 8, 16, 16)))
                {
                    xp.AddXP(o.Amount);
                    Infra.Analytics.Log("xp_pickup", new System.Collections.Generic.Dictionary<string, object>{{"t", Raylib.GetTime()},{"amt", o.Amount}});
                    o.Active = false;
                }
            }
        }

        public void Draw()
        {
            for (int i = 0; i < pool.Length; i++) pool[i].Draw();
        }
    }

    // Pact overlay (altar: buff + debuff)
    internal sealed class PactOverlay
    {
        private Rectangle[] rects = Array.Empty<Rectangle>();
        private string[] titles = new[] {
            "Storm Pact (permanent): +20% fire rate, +15% enemy speed",
            "Stone Pact (permanent): Heal to full and +20% Max HP, -15% move speed",
            "Blood Oath (permanent): +40% damage, -15% Max HP",
            "Greed Pact (30s): +40% XP gain, +20% elite chance",
            "Time Bargain (permanent): -20% dash cooldown, +10% enemy speed"
        };
        private int[] shown = Array.Empty<int>();
        public bool Opened { get; private set; }

        public void Open()
        {
            Opened = true;
            int x = 180; int y = 150; int w = 420; int h = 150; int gap = 40;
            rects = new[] { new Rectangle(x, y, w, h), new Rectangle(x + w + gap, y, w, h) };
            // pick 2 out of titles
            shown = ShufflePick(Enumerable.Range(0, titles.Length).ToArray(), 2);
        }

        public void Draw()
        {
            // Slight red tint to underline it's a pact, not a normal level up
            Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), new Color(140, 0, 0, 120));
            for (int i = 0; i < rects.Length; i++)
            {
                var r = rects[i];
                bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
                var bg = hover ? new Color(140, 140, 140, 255) : new Color(90, 90, 90, 240);
                Raylib.DrawRectangleRec(r, bg);
                Raylib.DrawRectangleLinesEx(r, 2, hover ? Color.Gold : Color.Black);
                int k = shown[i];
                DrawTextWrappedWithShadow($"{i+1}. {titles[k]}", r, 22, 6, Color.RayWhite);
            }
        }

        private static void DrawTextWrappedWithShadow(string text, Rectangle bounds, int fontSize, int lineSpacing, Color color)
        {
            int margin = 12;
            int maxWidth = (int)bounds.Width - margin * 2;
            var words = text.Split(' ');
            string line = string.Empty;
            int x = (int)bounds.X + margin;
            int y = (int)bounds.Y + margin;
            for (int wi = 0; wi < words.Length; wi++)
            {
                string test = string.IsNullOrEmpty(line) ? words[wi] : line + " " + words[wi];
                int width = Raylib.MeasureText(test, fontSize);
                if (width > maxWidth)
                {
                    // draw current line
                // shadow
                Game.Fonts.DrawText(line, x + 1, y + 1, fontSize, Color.Black);
                // main
                Game.Fonts.DrawText(line, x, y, fontSize, color);
                    y += fontSize + lineSpacing;
                    line = words[wi];
                }
                else
                {
                    line = test;
                }
            }
            if (!string.IsNullOrEmpty(line))
            {
                Game.Fonts.DrawText(line, x + 1, y + 1, fontSize, Color.Black);
                Game.Fonts.DrawText(line, x, y, fontSize, color);
            }
        }

        public int HitTest(Vector2 mouse)
        {
            for (int i = 0; i < rects.Length; i++) if (Raylib.CheckCollisionPointRec(mouse, rects[i])) return i; return -1;
        }

        public void Apply(int idx, ref Player player, EnemySpawner spawner)
        {
            int choice = shown[idx];
            if (choice == 0) // Storm Pact
            {
                player.ApplyMod(Player.ModType.ShootInterval);
                // enemies faster
                ModifyEnemies(spawner, speedMul: 1.15f);
                PactRuntime.AddPermanent("Storm Pact: +20% fire rate, +15% enemy speed");
            }
            else if (choice == 1) // Stone Pact (permanent, heals to full and increases max HP)
            {
                player.IncreaseMaxHPByPercent(0.20f, healToFull: true);
                player.ApplyMod(Player.ModType.MoveSpeed); // then nerf move by 15%
                player.PlayerSpeedMultiplier *= 0.85f;
                PactRuntime.AddPermanent("Stone Pact: Heal to full, +20% Max HP, -15% move speed");
            }
            else if (choice == 2) // Blood Oath
            {
                player.ApplyMod(Player.ModType.BulletDamage, 2); // +40% from mods, then nerf max HP
                player.IncreaseMaxHPByPercent(-0.15f, healToFull: false);
                PactRuntime.AddPermanent("Blood Oath: +25% bullet damage, -15% Max HP");
            }
            else if (choice == 3) // Greed Pact
            {
                Game.PactRuntime.ApplyTimed(PactEffect.XPGain, duration: 30f, strength: 0.40f);
                Game.PactRuntime.ApplyTimed(PactEffect.EliteChance, duration: 30f, strength: 0.20f);
            }
            else if (choice == 4) // Time Bargain
            {
                player.ApplyMod(Player.ModType.DashCooldown, 2); // -20%
                ModifyEnemies(spawner, speedMul: 1.10f);
                PactRuntime.AddPermanent("Time Bargain: -20% dash cooldown, +10% enemy speed");
            }
            Opened = false;
        }

        private void ModifyEnemies(EnemySpawner spawner, float speedMul = 1f)
        {
            var list = (List<Enemy>)typeof(EnemySpawner).GetField("enemies", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(spawner)!;
            foreach (var e in list) e.Speed *= speedMul;
        }

        private static int[] ShufflePick(int[] arr, int count)
        {
            var rnd = new Random();
            for (int i = arr.Length - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
            return arr.Take(count).ToArray();
        }
    }

    internal sealed class EnemySpawner
    {
        private readonly List<Enemy> enemies = new();
        private float timer;
        private float interval = 1.2f;
        private int maxAlive = 40;
        private float threat; // grows with time
        private int batchSize = 1;
        private float eliteChanceBonus = 0f; // from timed pacts
        // Breathing waves: short relief every period
        private float breatheTimer = 0f;
        private const float BreathePeriodSec = 20f;
        private int breatheTicksRemaining = 0; // number of spawn acts with longer interval

        public IReadOnlyList<Enemy> Enemies => enemies;
        public int Count => enemies.Count;

        public void Update(float dt, Vector2 target, Camera2D camera)
        {
            timer += dt;
            threat += dt;
            // escalation (smoother growth)
            float baseInterval = MathF.Max(0.5f, 1.25f - threat * 0.009f);
            maxAlive = 28 + (int)MathF.Min(72, threat * 0.75f);
            batchSize = (threat > 30f) ? 2 : 1;
            if (threat > 60f) batchSize = 3;
            if (threat > 90f) batchSize = 4;

            // Breathing wave timer
            breatheTimer += dt;
            if (breatheTimer >= BreathePeriodSec)
            {
                breatheTimer = 0f;
                breatheTicksRemaining = Raylib.GetRandomValue(3, 4);
                Infra.Analytics.Log("breath_start", new System.Collections.Generic.Dictionary<string, object>{{"t", Raylib.GetTime()}});
            }
            interval = baseInterval * (breatheTicksRemaining > 0 ? 1.5f : 1f);

            if (timer >= interval && enemies.Count < maxAlive)
            {
                timer = 0f;
                int toSpawn = Math.Min(batchSize, Math.Max(1, maxAlive - enemies.Count));
                for (int i = 0; i < toSpawn; i++) SpawnAtViewEdges(camera);
                if (breatheTicksRemaining > 0) breatheTicksRemaining--;
            }

            foreach (var e in enemies)
            {
                // Elite berserk speed boost when low HP
                float spd = e.Speed;
                if (e.IsElite && e.Modifier == EnemyMod.Berserk && e.HP < e.MaxHP * 0.5f)
                {
                    e.Speed = spd * 1.5f;
                }
                e.Update(dt, target);
                e.Speed = spd; // restore base for next frame
            }
            enemies.RemoveAll(e => !e.Alive);
        }

        public void Draw(Vector2 playerPos)
        {
            foreach (var e in enemies)
            {
                // Face towards player
                if (e.Alive)
                {
                    Vector2 dir = playerPos - e.Position;
                    if (dir.LengthSquared() > 1e-5f)
                    {
                        float angle = MathF.Atan2(dir.Y, dir.X);
                        e.GetType().GetField("faceAngleDeg", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                            .SetValue(e, angle * (180f/MathF.PI));
                    }
                }
                e.Draw();
            }
        }

        private void SpawnAtViewEdges(Camera2D camera)
        {
            float left = camera.Target.X - camera.Offset.X;
            float top = camera.Target.Y - camera.Offset.Y;
            float right = left + (camera.Offset.X * 2f);
            float bottom = top + (camera.Offset.Y * 2f);

            var rnd = Raylib.GetRandomValue(0, 3);
            Vector2 pos = rnd switch
            {
                0 => new Vector2(left - 32, Raylib.GetRandomValue((int)top, (int)bottom)),
                1 => new Vector2(right + 32, Raylib.GetRandomValue((int)top, (int)bottom)),
                2 => new Vector2(Raylib.GetRandomValue((int)left, (int)right), top - 32),
                _ => new Vector2(Raylib.GetRandomValue((int)left, (int)right), bottom + 32),
            };
            int tankChance = (int)MathF.Min(50, 20 + threat * 0.2f);
            int sprinterChance = (int)MathF.Min(40, 10 + threat * 0.15f);
            int eliteChance = (int)MathF.Min(30, 5 + threat * 0.1f + eliteChanceBonus * 100f);
            int roll = Raylib.GetRandomValue(0, 99);
            bool spawnTank = roll < tankChance;
            bool spawnSprinter = !spawnTank && roll < (tankChance + sprinterChance);
            var e = new Enemy { Position = pos };
            if (spawnTank)
            {
                e.IsTank = true;
                e.Speed = 70f;
                e.MaxHP = 60f;
                e.HP = 60f;
            }
            else if (spawnSprinter)
            {
                e.IsSprinter = true;
                e.Speed = 180f;
                e.MaxHP = 14f;
                e.HP = 14f;
            }
            // Elite?
            if (Raylib.GetRandomValue(0, 99) < eliteChance)
            {
                e.IsElite = true;
                e.Modifier = (EnemyMod)(Raylib.GetRandomValue(1, 2)); // Shielded or Berserk
                e.MaxHP *= 1.5f; e.HP *= 1.5f;
            }
            enemies.Add(e);
        }

        public void SetEliteChanceBonus(float bonus)
        {
            eliteChanceBonus = MathF.Max(0f, bonus);
        }
    }

    internal static class Collision
    {
        public static void Resolve(ProjectilePool projectiles, IReadOnlyList<Enemy> enemies, System.Action<Enemy>? onEnemyKilled = null, BossManager? boss = null)
        {
            foreach (var p in projectiles.All())
            {
                if (!p.Active) continue;
                Rectangle pb = p.GetBounds();
                for (int i = 0; i < enemies.Count; i++)
                {
                    var e = enemies[i];
                    if (!e.Alive) continue;
                    if (Raylib.CheckCollisionRecs(pb, e.GetBounds()))
                    {
                        float dmg = p.Damage;
                        // Keystone Darkness: enemies take reduced damage
                        if (KeystoneRuntime.IsActiveDarkness)
                        {
                            dmg *= 0.85f; // -15% damage taken
                        }
                        e.TakeDamage(dmg);
                        Game.FloatingTextSystem.Spawn(e.Position, $"{dmg:0}", Color.Yellow);
                        p.Active = false;
                        if (!e.Alive)
                        {
                            Infra.Analytics.Log("enemy_kill", new System.Collections.Generic.Dictionary<string, object>{{"t", Raylib.GetTime()}});
                            onEnemyKilled?.Invoke(e);
                        }
                        break;
                    }
                }
                // Boss collision
                if (boss != null && boss.Alive && Raylib.CheckCollisionRecs(pb, boss.Bounds))
                {
                    Infra.Analytics.Log("projectile_hit_boss", new System.Collections.Generic.Dictionary<string, object>{{"t", Raylib.GetTime()}});
                    boss.TakeDamage(p.Damage);
                    Game.FloatingTextSystem.Spawn(boss.Bounds.Position, $"{p.Damage:0}", Color.Orange);
                    p.Active = false;
                }
            }
        }
    }
}
