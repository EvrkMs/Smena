using Grpc.Core;
using Host.Grpc.Services.SendPhoto;
using Host.Services.Photo;

namespace Host.Services;

public class GrpcSendPhotoService(TelegramPhotoRequestService photoRequestService)
    : Host.Grpc.Services.SendPhoto.SendPhotoService.SendPhotoServiceBase
{
    private readonly TelegramPhotoRequestService _photoRequestService = photoRequestService;

    public override async Task RequestPhotos(
        RequestPhotosRequest request,
        IServerStreamWriter<PhotoStatusUpdate> responseStream,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.EmployeeId, out var employeeId))
        {
            await responseStream.WriteAsync(new PhotoStatusUpdate
            {
                Error = new Error { Message = "Invalid employee_id." }
            });
            return;
        }

        async Task HandleProgress(string message)
        {
            if (message.StartsWith("Received", StringComparison.OrdinalIgnoreCase))
            {
                var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], out var count))
                {
                    await responseStream.WriteAsync(new PhotoStatusUpdate
                    {
                        PhotosReceived = new PhotosReceived { ReceivedCount = count }
                    });
                    return;
                }
            }

            if (message == "Start")
            {
                await responseStream.WriteAsync(new PhotoStatusUpdate
                {
                    Start = new PhotoStart { Message = message }
                });
                return;
            }

            await responseStream.WriteAsync(new PhotoStatusUpdate
            {
                RequestSent = new PhotoRequestSent { Message = message }
            });
        }

        try
        {
            var result = await _photoRequestService.RequestPhotosAsync(
                employeeId,
                HandleProgress,
                context.CancellationToken);

            if (!result.Success)
            {
                await responseStream.WriteAsync(new PhotoStatusUpdate
                {
                    Timeout = new PhotosTimeout { Message = result.Message }
                });
                return;
            }

            await responseStream.WriteAsync(new PhotoStatusUpdate
            {
                PhotosReady = new PhotosReady { SessionKey = result.SessionKey ?? string.Empty }
            });
        }
        catch (Exception ex)
        {
            await responseStream.WriteAsync(new PhotoStatusUpdate
            {
                Error = new Error { Message = ex.Message }
            });
        }
    }
}
