using Raylib_cs;
using System.Numerics;
using EchoesGame.Infra;

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

        var player = new Game.Player(new Vector2(0, 0));
        var enemySpawner = new Game.EnemySpawner();
        var projectilePool = new Game.ProjectilePool(capacity: 512);
        var camera = new Camera2D
        {
            Target = player.Position,
            Offset = new Vector2(WindowWidth / 2f, WindowHeight / 2f),
            Rotation = 0,
            Zoom = 1f
        };
        bool paused = false;
        EchoesGame.Infra.Analytics.Init();
        EchoesGame.Infra.Analytics.Log("start_run", new System.Collections.Generic.Dictionary<string, object>());

        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime();

            if (Raylib.IsKeyPressed(KeyboardKey.P)) paused = !paused;
            if (paused)
            {
                // Draw only
                Raylib.BeginDrawing();
                Raylib.ClearBackground(Color.Black);
                camera.Target = player.Position;
                Raylib.BeginMode2D(camera);
                DrawFloorTiles();
                DrawWorldWalls();
                enemySpawner.Draw();
                projectilePool.Draw();
                player.Draw(camera);
                Raylib.EndMode2D();
                DrawHud(player, paused, projectilePool.ActiveCount, enemySpawner.Count);
                Raylib.EndDrawing();
                continue;
            }

            // Update
            player.Update(dt, projectilePool, camera);
            enemySpawner.Update(dt, player.Position, camera);
            projectilePool.Update(dt);
            Game.Collision.Resolve(projectilePool, enemySpawner.Enemies);

            // Draw
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);

            camera.Target = player.Position;
            Raylib.BeginMode2D(camera);
            DrawFloorTiles();
            DrawWorldWalls();
            enemySpawner.Draw();
            projectilePool.Draw();
            player.Draw(camera);
            Raylib.EndMode2D();

            DrawHud(player, paused, projectilePool.ActiveCount, enemySpawner.Count);

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

    private static void DrawHud(Game.Player player, bool paused, int bullets, int enemies)
    {
        int y = 16;
        Raylib.DrawText($"FPS: {Raylib.GetFPS()}", 16, y, 18, Color.RayWhite); y += 20;
        Raylib.DrawText($"Dash CD: {player.DashCooldownRemaining:0.00}s", 16, y, 18, Color.RayWhite); y += 20;
        Raylib.DrawText($"Bullets: {bullets}  Enemies: {enemies}", 16, y, 18, Color.RayWhite); y += 20;
        // Dash readiness bar (0..1)
        float readiness = 1f - (player.DashCooldownRemaining / player.DashCooldownTotal);
        if (readiness < 0f) readiness = 0f; if (readiness > 1f) readiness = 1f;
        DrawBar(16, y + 6, 256, 16, readiness);
        y += 26;
        if (paused)
        {
            Raylib.DrawText("PAUSED (P)", 16, y, 20, Color.Gold);
        }
    }

    private static void DrawBar(int x, int y, int width, int height, float normalized)
    {
        if (Assets.TryGet("ui_bar_bg.png", out var bg) && Assets.TryGet("ui_bar_fg.png", out var fg))
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
    using Raylib_cs;
    using System.Numerics;
    using System;
    using System.Collections.Generic;

    internal sealed class Player
    {
        public Vector2 Position { get; private set; }
        private Vector2 velocity;
        private const float MoveSpeed = 300f;
        private const float DashSpeed = 1100f;
        private const float DashDuration = 0.20f; // seconds of iFrames
        private const float DashCooldown = 4.0f;

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
                velocity = input * MoveSpeed;
                Position += velocity * dt;

                // Clamp to world bounds
                float halfW = Config.WorldWidth / 2f - 16f;
                float halfH = Config.WorldHeight / 2f - 16f;
                Position = new Vector2(Config.Clamp(Position.X, -halfW, halfW), Config.Clamp(Position.Y, -halfH, halfH));

                // Dash start
                if (dashCooldownTimer >= DashCooldown && Raylib.IsKeyPressed(KeyboardKey.LeftShift))
                {
                    isDashing = true;
                    dashTimer = 0f;
                    dashDirection = input.LengthSquared() < 1e-5f ? new Vector2(1, 0) : Vector2.Normalize(input);
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
        private const float ShootInterval = 0.12f; // auto-fire

        private void HandleShooting(float dt, ProjectilePool projectiles, Camera2D camera)
        {
            shootTimer += dt;
            if (shootTimer >= ShootInterval)
            {
                shootTimer = 0f;
                // Direction towards mouse in world space
                Vector2 mouseScreen = Raylib.GetMousePosition();
                Vector2 mouseWorld = Raylib.GetScreenToWorld2D(mouseScreen, camera);
                Vector2 dir = mouseWorld - Position;
                if (dir.LengthSquared() < 1e-5f) dir = new Vector2(1, 0);
                dir = Vector2.Normalize(dir);
                projectiles.Spawn(Position, dir * 900f);
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
                Raylib.DrawTexturePro(body, src, dst, origin, angleDeg, Color.White);
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

        public void Spawn(Vector2 pos, Vector2 vel)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                next = (next + 1) % pool.Length;
                if (!pool[next].Active)
                {
                    pool[next].Active = true;
                    pool[next].Position = pos;
                    pool[next].Velocity = vel;
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
        private const float Speed = 110f;
        private const float Radius = 12f;
        public bool Alive = true;

        public void Update(float dt, Vector2 target)
        {
            if (!Alive) return;
            Vector2 dir = target - Position;
            if (dir.LengthSquared() > 1e-5f)
            {
                dir = Vector2.Normalize(dir);
                Position += dir * Speed * dt;
            }
        }

        public void Draw()
        {
            if (!Alive) return;
            if (Assets.TryGet("enemy_chaser.png", out var tex))
            {
                var src = new Rectangle(0, 0, tex.Width, tex.Height);
                var dst = new Rectangle(Position.X, Position.Y, tex.Width, tex.Height);
                var origin = new Vector2(tex.Width / 2f, tex.Height / 2f);
                Raylib.DrawTexturePro(tex, src, dst, origin, 0, Color.White);
            }
            else
            {
                Raylib.DrawCircleV(Position, Radius, Color.Maroon);
            }
        }

        public Rectangle GetBounds() => new Rectangle(Position.X - Radius, Position.Y - Radius, Radius * 2, Radius * 2);
    }

    internal sealed class EnemySpawner
    {
        private readonly List<Enemy> enemies = new();
        private float timer;
        private const float Interval = 1.2f;

        public IReadOnlyList<Enemy> Enemies => enemies;
        public int Count => enemies.Count;

        public void Update(float dt, Vector2 target, Camera2D camera)
        {
            timer += dt;
            if (timer >= Interval)
            {
                timer = 0f;
                SpawnAtViewEdges(camera);
            }

            foreach (var e in enemies) e.Update(dt, target);
            enemies.RemoveAll(e => !e.Alive);
        }

        public void Draw()
        {
            foreach (var e in enemies) e.Draw();
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
            enemies.Add(new Enemy { Position = pos });
        }
    }

    internal static class Collision
    {
        public static void Resolve(ProjectilePool projectiles, IReadOnlyList<Enemy> enemies)
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
                        e.Alive = false;
                        p.Active = false;
                        break;
                    }
                }
            }
        }
    }
}
