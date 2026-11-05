using Fowan.Todo.Shared.Models;

namespace Fowan.Todo.Shared.Services;

public interface ITodoRepository
{
    TodoData LoadData();
    TodoSettings LoadSettings();
    void SaveData(TodoData data);
    void SaveSettings(TodoSettings settings);
    bool UpdateData(Func<TodoData, TodoSettings, bool> update);
}

public sealed class TodoPersistenceController(ITodoRepository repository)
{
    public static TodoPersistenceController CreateDefault() => new(new TodoStore());

    public TodoData LoadData() => repository.LoadData();

    public TodoSettings LoadSettings() => repository.LoadSettings();

    public void SaveData(TodoData data) => repository.SaveData(data);

    public void SaveSettings(TodoSettings settings) => repository.SaveSettings(settings);

    public bool UpdateData(Func<TodoData, TodoSettings, bool> update) => repository.UpdateData(update);

    public string CreateTaskId() => TodoStore.NewId("task");

    public string CreateListId() => TodoStore.NewId("list");

    public string DefaultListId => TodoStore.DefaultListId;
}
