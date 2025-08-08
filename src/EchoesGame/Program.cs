using Raylib_cs;
using System.Numerics;

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

        var player = new Game.Player(new Vector2(WindowWidth / 2f, WindowHeight / 2f));
        var enemySpawner = new Game.EnemySpawner();
        var projectilePool = new Game.ProjectilePool(capacity: 512);

        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime();

            // Update
            player.Update(dt, projectilePool);
            enemySpawner.Update(dt, player.Position);
            projectilePool.Update(dt);
            Game.Collision.Resolve(projectilePool, enemySpawner.Enemies);

            // Draw
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);

            DrawArenaBounds();
            enemySpawner.Draw();
            projectilePool.Draw();
            player.Draw();

            DrawHud(player);

            Raylib.EndDrawing();
        }

        Raylib.CloseWindow();
    }

    private static void DrawArenaBounds()
    {
            Raylib.DrawRectangleLines(32, 32, WindowWidth - 64, WindowHeight - 64, Color.DarkGray);
    }

    private static void DrawHud(Game.Player player)
    {
        string dash = $"Dash CD: {player.DashCooldownRemaining:0.00}s";
        Raylib.DrawText(dash, 24, 24, 18, Color.RayWhite);
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

        public Player(Vector2 start)
        {
            Position = start;
        }

        public void Update(float dt, ProjectilePool projectiles)
        {
            UpdateMovement(dt);
            HandleShooting(dt, projectiles);
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

                // Arena clamp
                Position = Vector2.Clamp(Position, new Vector2(48, 48), new Vector2(1280 - 48, 720 - 48));

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
                Position = Vector2.Clamp(Position, new Vector2(48, 48), new Vector2(1280 - 48, 720 - 48));
            }
        }

        private float shootTimer;
        private const float ShootInterval = 0.12f; // auto-fire

        private void HandleShooting(float dt, ProjectilePool projectiles)
        {
            shootTimer += dt;
            if (shootTimer >= ShootInterval)
            {
                shootTimer = 0f;
                // Direction towards mouse for now
                Vector2 mouse = Raylib.GetMousePosition();
                Vector2 dir = mouse - Position;
                if (dir.LengthSquared() < 1e-5f) dir = new Vector2(1, 0);
                dir = Vector2.Normalize(dir);
                projectiles.Spawn(Position, dir * 900f);
            }
        }

        public void Draw()
        {
            Color color = isDashing ? Color.Gold : Color.SkyBlue;
            Raylib.DrawCircleV(Position, 14f, color);
        }
    }

    internal sealed class Projectile
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public bool Active;
        private const float Radius = 4f;

        public void Update(float dt)
        {
            if (!Active) return;
            Position += Velocity * dt;
            if (Position.X < 32 || Position.X > 1280 - 32 || Position.Y < 32 || Position.Y > 720 - 32)
            {
                Active = false;
            }
        }

        public void Draw()
        {
            if (!Active) return;
            Raylib.DrawCircleV(Position, Radius, Color.Lime);
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
            Raylib.DrawCircleV(Position, Radius, Color.Maroon);
        }

        public Rectangle GetBounds() => new Rectangle(Position.X - Radius, Position.Y - Radius, Radius * 2, Radius * 2);
    }

    internal sealed class EnemySpawner
    {
        private readonly List<Enemy> enemies = new();
        private float timer;
        private const float Interval = 1.2f;

        public IReadOnlyList<Enemy> Enemies => enemies;

        public void Update(float dt, Vector2 target)
        {
            timer += dt;
            if (timer >= Interval)
            {
                timer = 0f;
                SpawnAtEdges(target);
            }

            foreach (var e in enemies) e.Update(dt, target);
            enemies.RemoveAll(e => !e.Alive);
        }

        public void Draw()
        {
            foreach (var e in enemies) e.Draw();
        }

        private void SpawnAtEdges(Vector2 target)
        {
            var rnd = Raylib.GetRandomValue(0, 3);
            Vector2 pos = rnd switch
            {
                0 => new Vector2(48, Raylib.GetRandomValue(48, 720 - 48)),
                1 => new Vector2(1280 - 48, Raylib.GetRandomValue(48, 720 - 48)),
                2 => new Vector2(Raylib.GetRandomValue(48, 1280 - 48), 48),
                _ => new Vector2(Raylib.GetRandomValue(48, 1280 - 48), 720 - 48),
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
