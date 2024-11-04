/// See https://aka.ms/new-console-template for more information
/// Shelf files.
/// Run: 'p4 describe -S -dw [changelist]' > file
/// Run: p4revertunchanged file -debug


using System.Collections.Concurrent;
using System.Diagnostics;


bool debug = true;

string fnd = "==== ";
string prev = "";
string filename = "";
string root = "";


SemaphoreSlim semaphore = new SemaphoreSlim(50); // limit to 50 concurrent tasks
var tasks = new List<Task>();

/// Debug to the console.
static void log(string msg)
{
    Console.WriteLine($"{DateTimeOffset.UtcNow}: {msg}");
}

/// Reverts the unchanged files.
/// Parses output from 'p4 describe -S -dw [changelist]'
/// And reverts each file that does not have any changes.
async Task RevertUnchangedFiles(string filename)
{
    log($"RevertUnchangedFiles: filename: {filename}");

    string[] lines = File.ReadAllLines(filename);

    Dictionary<string, int> changed = new Dictionary<string, int>();
    string file = "";
    lines.ToList().ForEach(line =>
    {
        if (line.Trim().StartsWith("==== ") && line.Trim().EndsWith("===="))
        {
            int start = line.IndexOf(fnd);
            if (start > -1)
            {
                int end = line.IndexOf("#", start + 1);
                if (end > -1)
                {
                    file = line.Substring(start + fnd.Length, (end - start - fnd.Length));
                    //log($"file: {file}");
                    if (!changed.ContainsKey(file)) changed[file] = 0;
                } //if
            } //if
        } //if
        else
        {
            if (line.Trim().Length > 1)
            {
                if (changed.ContainsKey(file)) changed[file] += 1;
            }// if
        } //else
    });

    changed.Keys.ToList().ForEach(key =>
    {
        log($"file: {key,-128}: {changed[key],5}");
    });

    changed = changed.Where(pair => pair.Value < 1).ToDictionary(pair => pair.Key, pair => pair.Value);

    string[] keys = changed.Keys.ToArray();
    var ranges = Partitioner.Create(0, keys.Length, 20).GetDynamicPartitions();
    foreach (var range in ranges)
    {
        tasks.Add(Task.Run(async () =>
        {
            for (int i = range.Item1; i < range.Item2; i++)
            {
                string key = keys[i];
                await semaphore.WaitAsync();
                try
                {
                    log($"File DID NOT Change ( reverting ): {key}");
                    await ExecuteCommandAsync($"p4 revert {key}");
                }
                finally
                {
                    semaphore.Release();
                }
            }
        }));
    }

    await Task.WhenAll(tasks);

    // changed.Keys.ToList().ForEach(key =>
    // {
    //     log($"File DID NOT Change ( reverting ): {key}");
    //     // we dont care about return code for now.
    //     ExecuteCommand($"p4 revert {key}");
    // });
}

async Task ExecuteCommandAsync(string command)
{
    // Your asynchronous command execution logic here
    await Task.Run(() => ExecuteCommand(command));
}

/// Executes a command.
static int ExecuteCommand(string command)
{
    log($"ExecuteCommand: {command}");
    int exitCode;
    ProcessStartInfo processInfo;
    Process process;

    processInfo = new ProcessStartInfo("cmd.exe", "/c " + command);
    processInfo.CreateNoWindow = true;
    processInfo.UseShellExecute = false;
    processInfo.RedirectStandardError = true;
    processInfo.RedirectStandardOutput = true;

    process = Process.Start(processInfo);
    process.WaitForExit();

    string output = process.StandardOutput.ReadToEnd();
    string error = process.StandardError.ReadToEnd();

    exitCode = process.ExitCode;
    process.Close();

    log($"ExecuteCommand: {command}, exitCode: {exitCode}");
    if (exitCode != 0)
    {
        log("=============== ERROR  ===============");
        log($"ExecuteCommand: {command}, error: {error}");
    } //if

    log("=============== OUTPUT ===============");
    log($"ExecuteCommand: {command}, output: \r\n{output}");
    log("=============== OUTPUT ===============\r\n");

    return exitCode;
}


try
{
    log($"{typeof(Program).Name} Arguments: [{string.Join(",", args)}]");
    if (args.Length < 1)
    {
        log($"This will revert all unchanged files from a changelist, by running a diff and ignoring any whitespace and line endings.");
        log("Arguments: [filename] [-debug]");
        log("filename must point to a file that contains the result of a p4 describe ( against a shelf ): 'p4 describe -S -dw [changelist] '");
        return -99;
    }

    filename = args[0];
    debug = (args.Length > 1 && args[1] == "-debug");
    log($"filename: {filename}, debug: {debug}");

    if (!File.Exists(filename)) throw new Exception($"filename: {filename} Does not exist.");
    ExecuteCommand("p4 set");
    await RevertUnchangedFiles(filename);

    return 0;

} //try
catch (Exception ex)
{
    log($"{typeof(Program).Name} Error: {ex}");
    return 99;
} //catch

