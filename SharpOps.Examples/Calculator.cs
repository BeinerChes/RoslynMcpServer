namespace SharpOps.Examples;

public class Calculator
{

    private readonly List<double> _history = new();
    /// <summary>
    /// Adds two integers and returns the result
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public int Add(int a, int b)
    {
        return a + b;
    }
    /// <summary>
    /// Adds a value to the history list
    /// </summary>
    /// <param name="value"></param>
    public void AddToHistory(double value)
    {
        _history.Add(value);
    }
    /// <summary>
    /// Returns the number of items in history
    /// </summary>
    /// <returns></returns>
    public int GetCount()
    {
        return _history.Count;
    }
    /// <summary>
    /// Clears the history list
    /// </summary>
    public void Clear()
    {
        _history.Clear();
    }
}