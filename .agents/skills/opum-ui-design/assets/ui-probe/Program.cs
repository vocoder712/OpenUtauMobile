using UiProbe;

string scenario = args.FirstOrDefault() ?? "basic";
try
{
    using ProbeHost host = new(scenario);
    switch (scenario)
    {
        case "basic": Scenarios.Basic(host); break;
        case "magnifier": Scenarios.Magnifier(host); break;
        case "settings": Scenarios.Settings(host); break;
        default: throw new ArgumentException("Unknown scenario: " + scenario);
    }
    Console.WriteLine("PROBE_OK " + scenario);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
