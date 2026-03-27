namespace POS.Services.IService;

public interface IStorageService
{
    Task<string> UploadAsync(Stream file, string fileName, string contentType);
    Task DeleteAsync(string url);
}
