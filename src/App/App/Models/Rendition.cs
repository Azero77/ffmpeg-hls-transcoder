namespace App.Models;

public record Rendition(
    int Resolution,
    byte Crf,
    int MaxBitrate,
    int Width,
    int Height,
    string OutFile
    );
    