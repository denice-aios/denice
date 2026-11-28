// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.CrossPlatform.Browser;

/// <summary>Converts one mono stream to another rate by linear interpolation, continuously across the buffers it is given.</summary>
internal sealed class LinearResampler
{
    private readonly double _step;
    private float _previous;
    private double _position;

    /// <summary>Initializes a new instance of the <see cref="LinearResampler"/> class.</summary>
    /// <param name="sourceRate">The input rate.</param>
    /// <param name="targetRate">The output rate.</param>
    internal LinearResampler(double sourceRate, double targetRate)
    {
        if (sourceRate <= 0 || targetRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRate), "Sample rates must be positive.");
        }

        _step = sourceRate / targetRate;
    }

    /// <summary>Converts the next input buffer.</summary>
    /// <param name="source">The input samples, continuing the previous buffer.</param>
    /// <returns>The output samples this buffer completes.</returns>
    internal float[] Process(ReadOnlySpan<float> source)
    {
        if (source.IsEmpty)
        {
            return [];
        }

        // Position is in input frames relative to this buffer; -1..0 interpolates from the previous buffer's last sample.
        var output = new List<float>((int)(source.Length / _step) + 2);
        while (_position < source.Length - 1)
        {
            int index = (int)Math.Floor(_position);
            double fraction = _position - index;
            float a = index < 0 ? _previous : source[index];
            float b = source[index + 1];
            output.Add((float)(a + ((b - a) * fraction)));
            _position += _step;
        }

        _position -= source.Length;
        _previous = source[^1];
        return [.. output];
    }
}
