using System;
using Godot;

public sealed class MarketPriceCurve
{
    public const int MinimumPriceCents = 100;
    public const int MaximumPriceCents = 2000;
    public const int InitialPriceCents = 500;

    private const double LongWeight = 0.58;
    private const double MediumWeight = 0.28;
    private const double ShortWeight = 0.12;
    private const double NoiseWeight = 0.02;
    private const double LongPeriodDays = 180.0;
    private const double MediumPeriodDays = 61.0;
    private const double ShortPeriodDays = 16.0;
    private readonly FastNoiseLite _noise;
    private readonly double _initialNoise;
    private readonly double _longPhase;
    private readonly double _mediumPhase;
    private readonly double _shortPhase;

    public MarketPriceCurve(int seed)
    {
        var random = new Random(seed);
        _mediumPhase = random.NextDouble() * 2.0 * Math.PI;
        _shortPhase = random.NextDouble() * 2.0 * Math.PI;
        double initialPosition = 2.0 * Math.Log((double)InitialPriceCents / MinimumPriceCents) /
            Math.Log((double)MaximumPriceCents / MinimumPriceCents) - 1.0;
        double initialLongSine = (initialPosition -
            MediumWeight * Math.Sin(_mediumPhase) -
            ShortWeight * Math.Sin(_shortPhase)) / LongWeight;
        double longPhase = Math.Asin(initialLongSine);
        _longPhase = random.Next(2) == 0 ? longPhase : Math.PI - longPhase;

        _noise = new FastNoiseLite
        {
            Seed = seed,
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            Frequency = 0.05f,
        };
        _initialNoise = SampleNoise(0.0f);
    }

    public int GetPriceCents(int day)
    {
        if (day < 1)
            throw new ArgumentOutOfRangeException(nameof(day));

        double elapsedDays = day - 1;
        double curve =
            LongWeight * Math.Sin(2.0 * Math.PI * elapsedDays / LongPeriodDays + _longPhase) +
            MediumWeight * Math.Sin(2.0 * Math.PI * elapsedDays / MediumPeriodDays + _mediumPhase) +
            ShortWeight * Math.Sin(2.0 * Math.PI * elapsedDays / ShortPeriodDays + _shortPhase) +
            NoiseWeight * (SampleNoise((float)elapsedDays) - _initialNoise) / 2.0;
        double position = (curve + 1.0) / 2.0;
        double price = MinimumPriceCents * Math.Pow(
            (double)MaximumPriceCents / MinimumPriceCents,
            position);
        return (int)Math.Round(price, MidpointRounding.AwayFromZero);
    }

    private double SampleNoise(float day)
    {
        return Math.Clamp(_noise.GetNoise1D(day), -1.0f, 1.0f);
    }
}
