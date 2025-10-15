using System.Collections.Immutable;
using System.Threading.Tasks;
using ConsoleAppFramework;
using Microsoft.Extensions.Logging;
using Ritgard.Mining;

var app = ConsoleApp.Create();
app.Add<Commands>();
await app.RunAsync(args);


internal class Commands
{
    public static readonly ILoggerFactory LoggerFactory
        = Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.AddSimpleConsole(c =>
        {
            c.SingleLine = true;
            c.TimestampFormat = "HH:mm:ss.fff ";
            c.UseUtcTimestamp = true;
        }));

    public static readonly ILogger Log
        = LoggerFactory.CreateLogger("Global");

    /// <summary>
    /// Mine a GitHub repo.
    /// </summary>
    /// <param name="repo">owner/repo_name</param>
    /// <param name="scope">The scope of the data mining.</param>
    /// <param name="cancellationToken">Optional token to cancel the async operation in progress.</param>
    [Command("repo")]
    public async Task MineRepo(
        [Argument] string repo,
        RepoMinerScope scope = RepoMinerScope.All,
        CancellationToken cancellationToken = default
    )
    {
        var (owner, repoName) = Utils.ParseRepoString(repo);
        var miner = new RepoMiner(
            logger: LoggerFactory.CreateLogger<RepoMiner>(),
            repoOwner: owner,
            repoName: repoName,
            scope: scope
        );
        var result = await miner.MineRepo(cancellationToken);
        if (result is not null)
        {
            var filename = $"{repoName.ToLower()}_{result.MiningCompletedAt:yyyy-MM-dd_HH-mm-ss}.json";
            Log.LogInformation("Saving to '{OutputPath}'.", filename);
            await Utils.WriteJson(
                result,
                filename,
                cancellationToken
            );
        }
    }

    // [Command("wtf")]
    // public void Wtf()
    // {
    //     var sw = new StringWriter();
    //     var md =
    //         "### Version\n\n2.1.0\n\n### Platform\n\nUbuntu 23.10, Deno 1.41\n\n### What steps will reproduce the bug?\n\n* Install Lume with Nav plugin.\r\n* Create directory tree as reported [here](https://lume.land/plugins/nav/#menu)\r\n* Create menu.vto and menu_item.vto as reported [here](https://lume.land/plugins/nav/#menu)\r\n* Create a layout.vto that import menu.vto (all in root directory, not in _templates_)\r\n* Execute lume -s\r\n\r\n\n\n### How often does it reproduce? Is there a required condition?\n\nAlways\n\n### What is the expected behavior?\n\nNo error.\n\n### What do you see instead?\n\n{{ include \u0022./menu.vto\u0022 }}\r\n\r\n\u003E Error in the template /_includes/menu.vto:4:7\r\n\r\n{{ include \u0022menu_item.vto\u0022 { item } }}\r\n\r\n\u003E Error in the template /_includes/menu_item.vto:10:3\r\n\r\n{{ for item of item.children }}\r\n\r\n\u003E Cannot access \u0027item\u0027 before initialization\r\n\r\n\r\n\r\n    at Environment.createError (https://deno.land/x/vento@v0.10.2/src/environment.ts:277:12)\r\n    at eval (eval at compile (https://deno.land/x/vento@v0.10.2/src/environment.ts:42:25), \u003Canonymous\u003E:24:23)\r\n    at eventLoopTick (ext:core/01_core.js:153:7)\r\n    at async Environment.runString (https://deno.land/x/vento@v0.10.2/src/environment.ts:87:14)\r\n    at async VentoEngine.render (https://deno.land/x/lume@v2.1.0/plugins/vento.ts:94:20)\r\n    at async Renderer.render (https://deno.land/x/lume@v2.1.0/core/renderer.ts:216:19)\r\n    at async Renderer.#renderLayout (https://deno.land/x/lume@v2.1.0/core/renderer.ts:293:17)\r\n    at async https://deno.land/x/lume@v2.1.0/core/renderer.ts:159:28\r\n    at async Promise.all (index 0)\r\n    at async concurrent (https://deno.land/x/lume@v2.1.0/core/utils/concurrent.ts:21:3)\r\nCaused by Error: Error in the template /_includes/menu.vto:4:7\r\n\r\n{{ include \u0022menu_item.vto\u0022 { item } }}\r\n\r\n\u003E Error in the template /_includes/menu_item.vto:10:3\r\n\r\n{{ for item of item.children }}\r\n\r\n\u003E Cannot access \u0027item\u0027 before initialization\r\n\r\n\r\n    at Environment.createError (https://deno.land/x/vento@v0.10.2/src/environment.ts:277:12)\r\n    at eval (eval at compile (https://deno.land/x/vento@v0.10.2/src/environment.ts:42:25), \u003Canonymous\u003E:26:23)\r\n    at eventLoopTick (ext:core/01_core.js:153:7)\r\n    at async Environment.run (https://deno.land/x/vento@v0.10.2/src/environment.ts:69:12)\r\n    at async eval (eval at compile (https://deno.land/x/vento@v0.10.2/src/environment.ts:42:25), \u003Canonymous\u003E:11:19)\r\n    at async Environment.runString (https://deno.land/x/vento@v0.10.2/src/environment.ts:87:14)\r\n    at async VentoEngine.render (https://deno.land/x/lume@v2.1.0/plugins/vento.ts:94:20)\r\n    at async Renderer.render (https://deno.land/x/lume@v2.1.0/core/renderer.ts:216:19)\r\n    at async Renderer.#renderLayout (https://deno.land/x/lume@v2.1.0/core/renderer.ts:293:17)\r\n    at async https://deno.land/x/lume@v2.1.0/core/renderer.ts:159:28\r\nCaused by Error: Error in the template /_includes/menu_item.vto:10:3\r\n\r\n{{ for item of item.children }}\r\n\r\n\u003E Cannot access \u0027item\u0027 before initialization\r\n\r\n    at Environment.createError (https://deno.land/x/vento@v0.10.2/src/environment.ts:277:12)\r\n    at eval (eval at compile (https://deno.land/x/vento@v0.10.2/src/environment.ts:42:25), \u003Canonymous\u003E:43:23)\r\n    at Environment.run (https://deno.land/x/vento@v0.10.2/src/environment.ts:69:18)\r\n    at eventLoopTick (ext:core/01_core.js:153:7)\r\n    at async eval (eval at compile (https://deno.land/x/vento@v0.10.2/src/environment.ts:42:25), \u003Canonymous\u003E:14:19)\r\n    at async Environment.run (https://deno.land/x/vento@v0.10.2/src/environment.ts:69:12)\r\n    at async eval (eval at compile (https://deno.land/x/vento@v0.10.2/src/environment.ts:42:25), \u003Canonymous\u003E:11:19)\r\n    at async Environment.runString (https://deno.land/x/vento@v0.10.2/src/environment.ts:87:14)\r\n    at async VentoEngine.render (https://deno.land/x/lume@v2.1.0/plugins/vento.ts:94:20)\r\n    at async Renderer.render (https://deno.land/x/lume@v2.1.0/core/renderer.ts:216:19)\r\nCaused by ReferenceError: Cannot access \u0027item\u0027 before initialization\r\n    at eval (eval at compile (https://deno.land/x/vento@v0.10.2/src/environment.ts:42:25), \u003Canonymous\u003E:27:41)\r\n    at Environment.run (https://deno.land/x/vento@v0.10.2/src/environment.ts:69:18)\r\n    at eventLoopTick (ext:core/01_core.js:153:7)\r\n    at async eval (eval at compile (https://deno.land/x/vento@v0.10.2/src/environment.ts:42:25), \u003Canonymous\u003E:14:19)\r\n    at async Environment.run (https://deno.land/x/vento@v0.10.2/src/environment.ts:69:12)\r\n    at async eval (eval at compile (https://deno.land/x/vento@v0.10.2/src/environment.ts:42:25), \u003Canonymous\u003E:11:19)\r\n    at async Environment.runString (https://deno.land/x/vento@v0.10.2/src/environment.ts:87:14)\r\n    at async VentoEngine.render (https://deno.land/x/lume@v2.1.0/plugins/vento.ts:94:20)\r\n    at async Renderer.render (https://deno.land/x/lume@v2.1.0/core/renderer.ts:216:19)\r\n    at async Renderer.#renderLayout (https://deno.land/x/lume@v2.1.0/core/renderer.ts:293:17)\n\n### Additional information\n\nAm I doing something wrong?\r\nI just followed the [official documentation](https://lume.land/plugins/nav/).";
    //     Utils.WriteMarkdownAsPlainText(md, sw);
    //     var result = sw.ToString();
    //     Console.WriteLine(result);
    // }
}
