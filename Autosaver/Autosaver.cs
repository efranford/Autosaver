using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace Autosaver
{
    class Autosaver
    {
        static void Main(string[] args)
        {
            try
            {
                //example args
                // -b test2 -d "C:\Autosaver Test" -m timer|10000 -h test
                Console.WriteLine("Optional arguments for Autosaver -b [branch name] -h [head branch for tip] -d [git directory optional] -f [filter *.* by default] -m [auto timer|time optional]");
                var arguments = GetArguments(args);
                var branchName = arguments.ContainsKey("b") ? arguments["b"] : "autosave_branch_" + DateTime.Now.ToFileTime();
                var headName = arguments.ContainsKey("h") ? arguments["h"] : "master";
                var folder = arguments.ContainsKey("d") ? arguments["d"] : Directory.GetCurrentDirectory();
                var filter = arguments.ContainsKey("f") ? arguments["f"] : "*.*";
                var mode = arguments.ContainsKey("m") ? arguments["m"] : "auto";
                if (Directory.Exists(folder))
                {
                    using (var repo = InitRepository(folder))
                    {
                        if (repo.Index.IsFullyMerged)
                        {
                            CreateBranch(repo, branchName, headName);
                            Commit(repo, new WaitForChangedResult { Name = "Starting new Autosaver session", ChangeType = WatcherChangeTypes.All });
                            Console.WriteLine("Watching {0} and committing to branch {1}", folder, branchName);
                            if (mode == "auto")
                            {
                                var watcher = new FileSystemWatcher(folder, filter) { IncludeSubdirectories = true };
                                do
                                {
                                    Commit(repo, watcher.WaitForChanged(WatcherChangeTypes.All));
                                } while (true);
                            }
                            else
                            {
                                var time = int.Parse(mode.Split('|')[1]);
                                var watcher = new FileSystemWatcher(folder, filter) { IncludeSubdirectories = true };
                                var changes = new List<WaitForChangedResult>();
                                var timer = new Timer(time);
                                timer.Enabled = true;
                                timer.Elapsed += (obj, evt) =>
                                {
                                    timer.Enabled = false;
                                    if (changes.Count > 0)
                                    {
                                        var changesCopy = new List<WaitForChangedResult>();
                                        changesCopy.AddRange(changes);
                                        changes.Clear();
                                        Commit(repo, changesCopy);
                                    }
                                    timer.Enabled = true;
                                };
                                do
                                {
                                    var changed = watcher.WaitForChanged(WatcherChangeTypes.All);
                                    if (!changed.Name.Contains(".git"))
                                    {
                                        changes.Add(changed);
                                    }
                                } while (true);
                            }
                        }
                        else
                        {
                            Console.WriteLine("There are existing changes in your repo. Please deal with them before starting the autosaver.");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("Invalid folder specified");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error has occurred.\nError: {0}\nPress any key to quit...", ex.Message);
                Console.ReadKey(false);
            }
        }

        private static Repository InitRepository(string folder)
        {
            try
            {
                var repository = new Repository(string.Format("{0}\\.git", folder));
                return repository;
            }
            catch (Exception ex)
            {
                Repository.Init(folder);
                return InitRepository(folder);
            }
        }

        private static Dictionary<string, string> GetArguments(string[] args)
        {
            if (args.Length % 2 != 0)
                throw new Exception("Must pass in values for all parameters");
            var results = new Dictionary<string, string>();
            for (int x = 0; x < args.Length; x++)
            {
                if (x % 2 == 0)
                {
                    results.Add(args[x].Replace("-", ""), args[x + 1]);
                }
            }
            return results;
        }

        private static void CreateBranch(Repository repo, string branchName, string headName)
        {
            var head = repo.Branches.Where(b => b.Name == headName).FirstOrDefault();
            var theBranch = repo.Branches.Where(b => b.Name == branchName).FirstOrDefault();
            if (theBranch == null)
            {
                if(head != null)
                {
                    var branch = repo.CreateBranch(branchName, head.Tip);
                    Console.WriteLine("Created new branch {0} with head of {1}", branch.Name, head.Tip.Sha);
                    branch = repo.Checkout(branch);
                    Console.WriteLine("Using branch {0}", branch.Name);
                }
                else
                {
                    var branch = repo.CreateBranch(branchName);
                    Console.WriteLine("Created new branch {0}", branch.Name);
                    branch = repo.Checkout(branch);
                    Console.WriteLine("Using branch {0}", branch.Name);
                }
            }
            else
            {
                try
                {
                    var branch = repo.Checkout(theBranch);
                    Console.WriteLine("Using branch {0}", branch.Name);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("There are existing changes in your repo. Please deal with them before starting the autosaver.");
                    throw ex;
                }
            }
        }

        private static void Commit(Repository repo, WaitForChangedResult waitForChangedResult)
        {
            if (!waitForChangedResult.Name.Contains(".git"))
            {
                switch (waitForChangedResult.ChangeType)
                {
                    case WatcherChangeTypes.Changed:
                    case WatcherChangeTypes.Created:
                        repo.Index.Stage(waitForChangedResult.Name);
                        break;
                    case WatcherChangeTypes.Deleted:
                        repo.Index.Remove(waitForChangedResult.Name);
                        break;
                    case WatcherChangeTypes.Renamed:
                        repo.Index.Stage(waitForChangedResult.Name);
                        repo.Index.Remove(waitForChangedResult.OldName);
                        break;
                    default:
                        break;
                }
                string message = string.Empty;
                if (waitForChangedResult.ChangeType == WatcherChangeTypes.All)
                {
                    message = "Starting new Autosaver session";
                }
                else
                {
                    message = waitForChangedResult.ChangeType == WatcherChangeTypes.Renamed ? string.Format("Renamed from {0} to {1}", waitForChangedResult.OldName, waitForChangedResult.Name) : waitForChangedResult.ChangeType.ToString();
                    message = string.Format("Autosaver - {0} - {1}", waitForChangedResult.Name, message);
                }
                var commit = repo.Commit(message);
                Console.WriteLine("Commit {0} - {1}", commit.Id.Sha, commit.Message);
            }
        }

        private static void Commit(Repository repo, List<WaitForChangedResult> changes)
        {
            var message = new StringBuilder();
            message.Append("Autosaver - Multiple Changes\n-----------------------------\n");
            foreach (var change in changes)
            {
                switch (change.ChangeType)
                {
                    case WatcherChangeTypes.Changed:
                    case WatcherChangeTypes.Created:
                        repo.Index.Stage(change.Name);
                        break;
                    case WatcherChangeTypes.Deleted:
                        repo.Index.Remove(change.Name);
                        break;
                    case WatcherChangeTypes.Renamed:
                        repo.Index.Stage(change.Name);
                        repo.Index.Remove(change.OldName);
                        break;
                    default:
                        break;
                }
                var theMessage = change.ChangeType == WatcherChangeTypes.Renamed ? string.Format("Renamed from {0} to {1}", change.OldName, change.Name) : string.Format("{0} - {1}", change.Name, change.ChangeType.ToString());
                message.Append(theMessage);
                message.Append("\n");
            }
            var commit = repo.Commit(message.ToString());
            Console.WriteLine("Commit {0} - {1}", commit.Id.Sha, commit.Message);
        }

    }
}
