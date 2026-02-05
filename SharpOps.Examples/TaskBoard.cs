namespace SharpOps.Examples;

public class TaskBoard
{

    private readonly List<TaskItem> _tasks = [];

    private int _nextId = 1;

    /// <summary>
    /// Gets the total number of tasks on the board
    /// </summary>
    public int Count => _tasks.Count;
    /// <summary>
    /// Creates a new task with auto-incremented ID, adds it to the board, and returns it.
    /// </summary>
    /// <param name="title"></param>
    /// <param name="priority"></param>
    /// <returns></returns>
    public TaskItem AddTask(string title, Priority priority)
    {
        var task = new TaskItem { Id = _nextId++, Title = title, Priority = priority };
        _tasks.Add(task);
        return task;
    }
    /// <summary>
    /// Returns the task with the given ID, or null if not found.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public TaskItem? GetTask(int id)
    {
        return _tasks.FirstOrDefault(t => t.Id == id);
    }
    /// <summary>
    /// Finds task by ID and marks it as completed. Returns true if found, false otherwise.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public bool CompleteTask(int id)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == id);
        if (task == null) return false;
        task.IsCompleted = true;
        return true;
    }
    /// <summary>
    /// Returns all incomplete tasks ordered by priority descending (Critical first).
    /// </summary>
    /// <returns></returns>
    public List<TaskItem> GetPendingTasks()
    {
        return _tasks.Where(t => !t.IsCompleted).OrderByDescending(t => t.Priority).ToList();
    }
    /// <summary>
    /// Removes a task by ID. Returns true if found and removed, false otherwise.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public bool RemoveTask(int id)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == id);
        if (task == null) return false;
        _tasks.Remove(task);
        return true;
    }
    /// <summary>
    /// Returns all tasks with the specified priority.
    /// </summary>
    /// <param name="priority"></param>
    /// <returns></returns>
    public List<TaskItem> GetByPriority(Priority priority)
    {
        return _tasks.Where(t => t.Priority == priority).ToList();
    }
    /// <summary>
    /// Returns the ratio of completed tasks to total tasks (0.0 to 1.0). Returns 0 if no tasks exist.
    /// </summary>
    /// <returns></returns>
    public double GetCompletionRate()
    {
        if (_tasks.Count == 0) return 0;
        return (double)_tasks.Count(t => t.IsCompleted) / _tasks.Count;
    }
}