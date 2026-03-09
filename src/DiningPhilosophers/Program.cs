using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

const int PhilosopherCount = 5;
const int DefaultDurationSeconds = 10;

while (true)
{
    Console.WriteLine("Choose mode:");
    Console.WriteLine("1) N=5: one counting semaphore (room=N-1) + forks (locks)");
    Console.WriteLine("2) N=5: one mutex + five semaphores for forks");
    Console.WriteLine("3) Exit");
    Console.Write("Enter choice (1-3): ");

    var rawChoice = Console.ReadLine();
    if (rawChoice is null)
    {
        return;
    }

    var choice = rawChoice.Trim();
    if (choice == "3")
    {
        return;
    }

    if (choice != "1" && choice != "2")
    {
        Console.WriteLine("Invalid choice. Try again.\n");
        continue;
    }

    var durationSeconds = ReadIntOrDefault("Simulation duration in seconds [10]: ", DefaultDurationSeconds);
    var seed = Environment.TickCount;

    var mode = choice == "1" ? SimulationMode.CountingSemaphoreRoom : SimulationMode.GlobalMutexWithForkSemaphores;

    Console.WriteLine();
    Console.WriteLine($"Starting simulation. Mode={mode}, Duration={durationSeconds}s");
    Console.WriteLine();

    var result = await RunSimulationAsync(mode, durationSeconds, seed);

    Console.WriteLine();
    Console.WriteLine("=== Statistics ===");
    for (var i = 0; i < result.EatCount.Length; i++)
    {
        Console.WriteLine($"Philosopher {i}: eatCount={result.EatCount[i]}");
    }

    Console.WriteLine();
}

static int ReadIntOrDefault(string prompt, int defaultValue)
{
    Console.Write(prompt);
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input))
    {
        return defaultValue;
    }

    return int.TryParse(input, out var value) && value > 0 ? value : defaultValue;
}

static async Task<SimulationResult> RunSimulationAsync(SimulationMode mode, int durationSeconds, int baseSeed)
{
    var eatCount = new int[PhilosopherCount];
    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
    var ct = cts.Token;

    var room = mode == SimulationMode.CountingSemaphoreRoom
        ? new SemaphoreSlim(PhilosopherCount - 1, PhilosopherCount - 1)
        : null;

    var forkSem = Enumerable.Range(0, PhilosopherCount)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    var globalMutex = new object();

    var tasks = new List<Task>();
    for (var id = 0; id < PhilosopherCount; id++)
    {
        var philosopherId = id;
        tasks.Add(Task.Run(() => PhilosopherLoopAsync(
            philosopherId,
            mode,
            room,
            forkSem,
            globalMutex,
            eatCount,
            baseSeed,
            ct)));
    }

    await Task.WhenAll(tasks);

    foreach (var sem in forkSem)
    {
        sem.Dispose();
    }

    room?.Dispose();

    return new SimulationResult(eatCount);
}

static async Task PhilosopherLoopAsync(
    int philosopherId,
    SimulationMode mode,
    SemaphoreSlim? room,
    SemaphoreSlim[] forkSem,
    object globalMutex,
    int[] eatCount,
    int baseSeed,
    CancellationToken ct)
{
    var leftFork = philosopherId;
    var rightFork = (philosopherId + 1) % PhilosopherCount;
    var rnd = new Random(unchecked(baseSeed + philosopherId * 997));

    while (!ct.IsCancellationRequested)
    {
        try
        {
            Log(philosopherId, "THINKING");
            await Task.Delay(rnd.Next(100, 501), ct);

            Log(philosopherId, "HUNGRY");

            var hasRoom = false;
            var hasLeft = false;
            var hasRight = false;

            try
            {
                if (mode == SimulationMode.CountingSemaphoreRoom)
                {
                    await room!.WaitAsync(ct);
                    hasRoom = true;

                    await forkSem[leftFork].WaitAsync(ct);
                    hasLeft = true;

                    await forkSem[rightFork].WaitAsync(ct);
                    hasRight = true;
                }
                else
                {
                    lock (globalMutex)
                    {
                        forkSem[leftFork].Wait(ct);
                        hasLeft = true;

                        try
                        {
                            forkSem[rightFork].Wait(ct);
                            hasRight = true;
                        }
                        catch
                        {
                            forkSem[leftFork].Release();
                            hasLeft = false;
                            throw;
                        }
                    }
                }

                Log(philosopherId, "EATING");
                await Task.Delay(rnd.Next(100, 501), ct);
                Interlocked.Increment(ref eatCount[philosopherId]);
            }
            finally
            {
                if (hasRight)
                {
                    forkSem[rightFork].Release();
                }

                if (hasLeft)
                {
                    forkSem[leftFork].Release();
                }

                if (hasRoom)
                {
                    room!.Release();
                }

                if (hasLeft || hasRight)
                {
                    Log(philosopherId, "PUT_FORKS");
                }
            }
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }
}

static void Log(int philosopherId, string state)
{
    lock (Sync.ConsoleLock)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] P{philosopherId} {state}");
    }
}

enum SimulationMode
{
    CountingSemaphoreRoom,
    GlobalMutexWithForkSemaphores
}

record SimulationResult(int[] EatCount);

static class Sync
{
    public static readonly object ConsoleLock = new();
}
