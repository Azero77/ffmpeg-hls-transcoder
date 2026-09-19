namespace App.Models;

public sealed record Rendition(
    int Resolution,
    byte Crf,
    int MaxBitrate,
    int Width,
    int Height,
    string NameModifier,
    string OutFile);