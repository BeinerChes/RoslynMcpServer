using SharpOps.Examples;

// Demo: TaskBoard built entirely through the SharpTinyCoder workflow
// Every method below was first attempted by the model (AddMember auto=true),
// then corrected by Claude Code (UpdateMethod) — generating training data.

var board = new TaskBoard();

// Add some tasks
var t1 = board.AddTask("Fix login bug", Priority.Critical);
var t2 = board.AddTask("Update README", Priority.Low);
var t3 = board.AddTask("Add unit tests", Priority.High);
var t4 = board.AddTask("Deploy to staging", Priority.Medium);
var t5 = board.AddTask("Review PR #42", Priority.High);

Console.WriteLine($"Board has {board.Count} tasks\n");

// Show pending tasks (sorted by priority)
Console.WriteLine("Pending tasks (by priority):");
foreach (var task in board.GetPendingTasks())
    Console.WriteLine($"  [{task.Priority,-8}] #{task.Id} {task.Title}");

// Complete some tasks
board.CompleteTask(t1.Id);
board.CompleteTask(t3.Id);

Console.WriteLine($"\nCompleted tasks #1 and #3");
Console.WriteLine($"Completion rate: {board.GetCompletionRate():P0}\n");

// Filter by priority
var highPriority = board.GetByPriority(Priority.High);
Console.WriteLine($"High priority tasks: {highPriority.Count}");
foreach (var task in highPriority)
    Console.WriteLine($"  #{task.Id} {task.Title} (completed: {task.IsCompleted})");

// Remove a task
board.RemoveTask(t2.Id);
Console.WriteLine($"\nRemoved #{t2.Id}, board now has {board.Count} tasks");

// Look up a task
var found = board.GetTask(t4.Id);
Console.WriteLine($"Found task #{found?.Id}: {found?.Title}");

var missing = board.GetTask(999);
Console.WriteLine($"Task #999: {(missing == null ? "not found" : missing.Title)}");
