namespace Host.Services.Photo;

public class TelegramUpdateOffsetStore
{
    private readonly object _lock = new();
    private int _offset;

    public int GetOffset()
    {
        lock (_lock)
        {
            return _offset;
        }
    }

    public void AdvanceOffset(int newOffset)
    {
        lock (_lock)
        {
            if (newOffset > _offset)
            {
                _offset = newOffset;
            }
        }
    }
}
