namespace MyMeteor;

// A structure to facilitate basic undo-redo semantics.
public class History<T>(int size)
{
    private readonly T[] _array = new T[size];
    public int FinalIndex { get; } = size - 1;
    public int CurrentIndex { get; private set; } = -1;

    public T? Current => CurrentIndex > -1 ? _array[CurrentIndex] : default;

    public T? Prev => CurrentIndex > 0 ? _array[CurrentIndex - 1] : default;

    public T? First => CurrentIndex > -1 ? _array[0] : default;

    public void Add(T item)
    {
        // If we're at capacity:
        if (CurrentIndex == FinalIndex)
        {
            // The element at index 0 falls off the back.
            for (int i = 0; i < CurrentIndex; i++)
                _array[i] = _array[i + 1];
        }

        else
        {
            CurrentIndex++;

            // Anything in front of the mark goes away.
            if (CurrentIndex < FinalIndex)
                Array.Clear(_array, CurrentIndex, FinalIndex - CurrentIndex);
        }

        // Record the item at the mark.
        _array[CurrentIndex] = item;
    }

    public void Clear() => Array.Clear(_array);

    public void WipeHistory()
    {
        T value = _array[CurrentIndex];
        Array.Clear(_array);
        _array[0] = value;
    }

    public void Back()
    {
        if (CurrentIndex > 0)
            CurrentIndex--;
    }

    public void Forward()
    {
        if (CurrentIndex < FinalIndex && _array[CurrentIndex + 1] != null)
            CurrentIndex++;
    }

    public ReadOnlySpan<T> AsSpan() => _array.AsSpan();
}