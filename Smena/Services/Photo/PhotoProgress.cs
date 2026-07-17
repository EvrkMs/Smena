namespace Host.Services.Photo;

/// <summary>
/// Типизированный прогресс фото-сценария. Раньше прогресс шёл человекочитаемыми
/// строками («Received 3 photos»), которые GrpcSendPhotoService парсил обратно
/// Split-ом — любое перефразирование молча ломало счётчик фото у клиента.
/// </summary>
public enum PhotoProgressStage
{
    Start,
    RequestSent,
    PhotosReceived
}

public sealed record PhotoProgress(PhotoProgressStage Stage, int ReceivedCount = 0);
