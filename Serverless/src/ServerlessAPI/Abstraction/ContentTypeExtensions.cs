namespace Lambda.Abstraction
{
    public static class ContentTypeExtensions
    {
        public static string ToMimeString(this ContentType contentType)
        {
            return contentType switch
            {
                ContentType.AudioMpeg => "audio/mpeg",
                ContentType.AudioWav => "audio/wav",
                ContentType.AudioFlac => "audio/flac",
                ContentType.ImageJpeg => "image/jpeg",
                ContentType.ImagePng => "image/png",
                ContentType.ApplicationOctet => "application/octet-stream",
                ContentType.ApplicationJson => "application/json",
                ContentType.TextPlain => "text/plain",
                _ => "application/octet-stream"
            };
        }
    }
}
