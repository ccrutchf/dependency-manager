using System.CommandLine;
using DependencyManager.Commands;
using DependencyManager.Config;

var configOption = new Option<string>("--config", "-c")
{
    Description = "Path to the YAML config file",
    DefaultValueFactory = _ => "packages.yaml",
};

var tagOption = new Option<string[]>("--tag")
{
    Description = "Activate a machine tag (repeatable, or comma-separated). Replaces DEPEND_TAGS when given",
};

var failFastOption = new Option<bool>("--fail-fast")
{
    Description = "Abort on the first failure (default: continue and summarize at end)",
};

var restartOption = new Option<bool>("--restart")
{
    Description = "Allow system updaters to reboot the machine when an update requires it",
};

var pruneOption = new Option<bool>("--prune")
{
    Description = "After installing, remove packages not in the declared plan (converge to config)",
};

var planPruneOption = new Option<bool>("--prune")
{
    Description = "Also preview the removals that install --prune would make",
};

var applyOption = new Option<bool>("--apply")
{
    Description = "Actually remove undeclared packages (default: dry run — list only)",
};

var planCmd = new Command("plan", "Print the resolved package plan without installing anything");
planCmd.Options.Add(configOption);
planCmd.Options.Add(tagOption);
planCmd.Options.Add(planPruneOption);
planCmd.SetAction((parseResult, ct) => PlanCommand.RunAsync(
    parseResult.GetValue(configOption)!,
    ActiveTags.FromEnvironment(parseResult.GetValue(tagOption)),
    parseResult.GetValue(planPruneOption),
    ct));

var installCmd = new Command("install", "Install every package in the resolved plan");
installCmd.Options.Add(configOption);
installCmd.Options.Add(tagOption);
installCmd.Options.Add(failFastOption);
installCmd.Options.Add(pruneOption);
installCmd.SetAction((parseResult, ct) => InstallCommand.RunAsync(
    parseResult.GetValue(configOption)!,
    ActiveTags.FromEnvironment(parseResult.GetValue(tagOption)),
    parseResult.GetValue(failFastOption),
    parseResult.GetValue(pruneOption),
    ct));

var testCmd = new Command("test", "Exit 0 if every package in the plan is installed, else 1");
testCmd.Options.Add(configOption);
testCmd.Options.Add(tagOption);
testCmd.SetAction((parseResult, ct) => TestCommand.RunAsync(
    parseResult.GetValue(configOption)!,
    ActiveTags.FromEnvironment(parseResult.GetValue(tagOption)),
    ct));

var pruneCmd = new Command("prune", "List (or with --apply, remove) installed packages not in the plan");
pruneCmd.Options.Add(configOption);
pruneCmd.Options.Add(tagOption);
pruneCmd.Options.Add(applyOption);
pruneCmd.SetAction((parseResult, ct) => PruneCommand.RunAsync(
    parseResult.GetValue(configOption)!,
    ActiveTags.FromEnvironment(parseResult.GetValue(tagOption)),
    parseResult.GetValue(applyOption),
    ct));

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
root.Subcommands.Add(pruneCmd);
root.Subcommands.Add(listCmd);
root.Subcommands.Add(updateCmd);

return await root.Parse(args).InvokeAsync();
