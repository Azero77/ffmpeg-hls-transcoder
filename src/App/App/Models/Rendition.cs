namespace App.Models;

public record Rendition(
    int Resolution,
    byte Crf,
    int Bitrate,
    int Width,
    int Height,
    string OutFile
    );
    