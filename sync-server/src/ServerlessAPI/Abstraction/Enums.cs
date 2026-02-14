namespace ServerAPI.Abstraction
{
    public enum ContentType
    {
        None = 0,
        AudioMpeg,        // audio/mpeg
        AudioWav,         // audio/wav
        AudioFlac,        // audio/flac
        ImageJpeg,        // image/jpeg
        ImagePng,
        ImageGif,// image/png
        ApplicationOctet, // application/octet-stream
        ApplicationJson,  // application/json
        TextPlain         // text/plain
    }
    public enum Role
    {
        User,
        Admin
    }
}
