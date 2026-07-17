using Grpc.Core;
using Host.Grpc.Services.SendPhoto;
using Host.Services.Photo;
using Microsoft.Extensions.Logging;

namespace Host.Services;

public class GrpcSendPhotoService(
    TelegramPhotoRequestService photoRequestService,
    ILogger<GrpcSendPhotoService> logger)
    : Host.Grpc.Services.SendPhoto.SendPhotoService.SendPhotoServiceBase
{
    private readonly TelegramPhotoRequestService _photoRequestService = photoRequestService;
    private readonly ILogger<GrpcSendPhotoService> _logger = logger;

    public override async Task RequestPhotos(
        RequestPhotosRequest request,
        IServerStreamWriter<PhotoStatusUpdate> responseStream,
        ServerCallContext context)
    {
        async Task<bool> TryWriteAsync(PhotoStatusUpdate update)
        {
            if (context.CancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Skipping photo stream write for employeeId={EmployeeId} because request is already canceled.",
                    request.EmployeeId);
                return false;
            }

            try
            {
                await responseStream.WriteAsync(update);
                return true;
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Photo stream write canceled for employeeId={EmployeeId}.",
                    request.EmployeeId);
                return false;
            }
            catch (InvalidOperationException) when (context.CancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Photo stream writer already completed for employeeId={EmployeeId}.",
                    request.EmployeeId);
                return false;
            }
        }

        if (!Guid.TryParse(request.EmployeeId, out var employeeId))
        {
            await TryWriteAsync(new PhotoStatusUpdate
            {
                Error = new Error { Message = "Invalid employee_id." }
            });
            return;
        }

        // Прогресс типизирован (PhotoProgress) — раньше сюда приходили строки
        // вида "Received 3 photos", которые парсились Split-ом обратно в oneof.
        async Task HandleProgress(PhotoProgress progress)
        {
            var update = progress.Stage switch
            {
                PhotoProgressStage.Start => new PhotoStatusUpdate
                {
                    Start = new PhotoStart { Message = "Start" }
                },
                PhotoProgressStage.PhotosReceived => new PhotoStatusUpdate
                {
                    PhotosReceived = new PhotosReceived { ReceivedCount = progress.ReceivedCount }
                },
                _ => new PhotoStatusUpdate
                {
                    RequestSent = new PhotoRequestSent { Message = "Request sent" }
                }
            };

            await TryWriteAsync(update);
        }

        try
        {
            var result = await _photoRequestService.RequestPhotosAsync(
                employeeId,
                HandleProgress,
                context.CancellationToken);

            if (!result.Success)
            {
                await TryWriteAsync(new PhotoStatusUpdate
                {
                    Error = new Error { Message = result.Message }
                });
                return;
            }

            await TryWriteAsync(new PhotoStatusUpdate
            {
                PhotosReady = new PhotosReady { SessionKey = result.SessionKey ?? string.Empty }
            });
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // Client disconnected or request was canceled; no response can be written anymore.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RequestPhotos failed for employeeId={EmployeeId}", request.EmployeeId);

            var detail = ex.InnerException == null
                ? ex.Message
                : $"{ex.Message} | Inner: {ex.InnerException.Message}";

            await TryWriteAsync(new PhotoStatusUpdate
            {
                Error = new Error { Message = detail }
            });
        }
    }
}
