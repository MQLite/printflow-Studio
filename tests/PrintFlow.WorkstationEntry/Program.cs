namespace PrintFlow.WorkstationEntry;

public static class Program
{
    [STAThread]
    public static int Main(string[] args) => EntryCommand.Execute(args, Console.Out);
}
