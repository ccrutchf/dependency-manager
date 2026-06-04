using System.CommandLine;
using DependencyManager.Commands;

var configOption = new Option<string>("--config", "-c")
{
    Description = "Path to the YAML config file",
    DefaultValueFactory = _ => "packages.yaml",
};

var failFastOption = new Option<bool>("--fail-fast")
{
    Description = "Abort on the first failure (default: continue and summarize at end)",
};

var restartOption = new Option<bool>("--restart")
{
    Description = "Allow system updaters to reboot the machine when an update requires it",
};

var planCmd = new Command("plan", "Print the resolved package plan without installing anything");
planCmd.Options.Add(configOption);
planCmd.SetAction(parseResult => PlanCommand.Run(parseResult.GetValue(configOption)!));

var installCmd = new Command("install", "Install every package in the resolved plan");
installCmd.Options.Add(configOption);
installCmd.Options.Add(failFastOption);
installCmd.SetAction((parseResult, ct) => InstallCommand.RunAsync(
    parseResult.GetValue(configOption)!,
    parseResult.GetValue(failFastOption),
    ct));

var testCmd = new Command("test", "Exit 0 if every package in the plan is installed, else 1");
testCmd.Options.Add(configOption);
testCmd.SetAction((parseResult, ct) => TestCommand.RunAsync(parseResult.GetValue(configOption)!, ct));

var listCmd = new Command("list", "Show which package managers are available on this machine");
listCmd.SetAction(_ => ListCommand.Run());

var updateCmd = new Command("update", "Update all packages via every available provider");
updateCmd.Options.Add(restartOption);
updateCmd.SetAction((parseResult, ct) => UpdateCommand.RunAsync(
    parseResult.GetValue(restartOption),
    ct));

var root = new RootCommand("depend — declarative package installer");
root.Subcommands.Add(planCmd);
root.Subcommands.Add(installCmd);
root.Subcommands.Add(testCmd);
root.Subcommands.Add(listCmd);
root.Subcommands.Add(updateCmd);

return await root.Parse(args).InvokeAsync();
