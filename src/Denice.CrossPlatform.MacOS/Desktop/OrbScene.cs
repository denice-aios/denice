// Copyright (c) Denice.ai. All rights reserved.

using SkiaSharp;

namespace Denice.CrossPlatform.Desktop;

/// <summary>Draws the shared logical-coordinate orb using the owning platform GPU context.</summary>
internal sealed class OrbScene : IDisposable
{
    /// <summary>The shared browser and scene logical width.</summary>
    internal const int Width = 1024;

    /// <summary>The shared browser and scene logical height.</summary>
    internal const int Height = 768;

    private readonly Particle[] _particles;
    private readonly SKImage _particleSprite;
    private readonly SKPoint[] _projected;
    private readonly SKRect[] _spriteRects;
    private readonly SKRotationScaleMatrix[] _spriteTransforms;
    private readonly SKColor[] _spriteColors;

    private OrbScene(GRContext context)
    {
        _particles = CreateParticles();
        _projected = new SKPoint[_particles.Length];
        _spriteRects = new SKRect[_particles.Length];
        _spriteTransforms = new SKRotationScaleMatrix[_particles.Length];
        _spriteColors = new SKColor[_particles.Length];
        Array.Fill(_spriteRects, SKRect.Create(16, 16));
        _particleSprite = CreateParticleSprite(context);
    }

    /// <inheritdoc />
    public void Dispose() => _particleSprite.Dispose();

    /// <summary>Creates the shared scene on the platform's GPU context.</summary>
    /// <param name="context">The borrowed context, which must outlive this scene.</param>
    /// <returns>The scene owning its particle sprite.</returns>
    internal static OrbScene Create(GRContext context) => new(context);

    /// <summary>Draws the animated orb in the shared 1024 by 768 logical scene.</summary>
    /// <param name="canvas">The destination GPU canvas.</param>
    /// <param name="seconds">Elapsed animation time in seconds.</param>
    internal void Draw(SKCanvas canvas, double seconds)
    {
        float breath = 1 + (0.035f * (float)Math.Sin(seconds * Math.PI / 2));
        float radius = 225 * breath;
        float angle = (float)(seconds * 0.045);
        using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
        using var glow = SKShader.CreateRadialGradient(new SKPoint(512, 384), radius * 1.25f, [new SKColor(16, 58, 105, 70), new SKColor(8, 23, 50, 25), SKColors.Transparent], [0, 0.65f, 1], SKShaderTileMode.Clamp);
        paint.Shader = glow;
        canvas.DrawCircle(512, 384, radius * 1.25f, paint);
        paint.Shader = null;
        float cosine = MathF.Cos(angle);
        float sine = MathF.Sin(angle);
        for (int index = 0; index < _particles.Length; index++)
        {
            (float px, float py, float pz) = _particles[index];
            float x = (px * cosine) + (pz * sine);
            float z = (-px * sine) + (pz * cosine);
            float perspective = 1 / (1 - (z * 0.18f));
            _projected[index] = new SKPoint(512 + (x * radius * perspective), 384 + (py * radius * perspective));
            byte alpha = (byte)(65 + ((z + 1) * 80));
            _spriteColors[index] = new SKColor(72, 175, 255, alpha);
            float spriteScale = ((0.65f + ((z + 1) * 0.65f)) * breath) / 7;
            _spriteTransforms[index] = new SKRotationScaleMatrix(spriteScale, 0, _projected[index].X - (8 * spriteScale), _projected[index].Y - (8 * spriteScale));
        }

        canvas.DrawAtlas(_particleSprite, _spriteRects, _spriteTransforms, _spriteColors, SKBlendMode.Modulate, new SKSamplingOptions(SKFilterMode.Linear), paint);
        paint.StrokeWidth = 0.55f;
        for (int index = 0; index < _particles.Length; index++)
        {
            for (int offset = 1; offset <= 5 && index + offset < _particles.Length; offset++)
            {
                SKPoint a = _projected[index];
                SKPoint b = _projected[index + offset];
                float distance = SKPoint.Distance(a, b);
                if (distance < 45)
                {
                    paint.Color = new SKColor(46, 139, 240, (byte)(50 * (1 - (distance / 45))));
                    canvas.DrawLine(a, b, paint);
                }
            }
        }
    }

    private static SKImage CreateParticleSprite(GRContext context)
    {
        using SKSurface sprite = SKSurface.Create(context, false, new SKImageInfo(16, 16, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (sprite.Handle == 0)
        {
            throw new InvalidOperationException("Cannot allocate the particle sprite on the GPU.");
        }

        sprite.Canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        sprite.Canvas.DrawCircle(8, 8, 7, paint);
        return sprite.Snapshot();
    }

    private static Particle[] CreateParticles()
    {
        const int count = 2000;
        var particles = new Particle[count];
        const double goldenAngle = Math.PI * (3 - 2.23606797749979);
        for (int index = 0; index < count; index++)
        {
            double y = 1 - ((index / (double)(count - 1)) * 2);
            double ring = Math.Sqrt(1 - (y * y));
            double theta = goldenAngle * index;
            particles[index] = new Particle((float)(Math.Cos(theta) * ring), (float)y, (float)(Math.Sin(theta) * ring));
        }

        return particles;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct Particle(float X, float Y, float Z);
}
