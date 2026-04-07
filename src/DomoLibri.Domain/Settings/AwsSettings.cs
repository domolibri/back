namespace DomoLibri.Domain.Settings;

public class AwsSettings
{
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string ServiceURL { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
}
