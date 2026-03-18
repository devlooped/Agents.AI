![Icon](assets/img/icon-32.png) Extensions for Microsoft.Agents.AI
============

[![Version](https://img.shields.io/nuget/vpre/Devlooped.Agents.AI.svg?color=royalblue)](https://www.nuget.org/packages/Devlooped.Agents.AI)
[![Downloads](https://img.shields.io/nuget/dt/Devlooped.Agents.AI.svg?color=darkmagenta)](https://www.nuget.org/packages/Devlooped.Agents.AI)
[![EULA](https://img.shields.io/badge/EULA-OSMF-blue?labelColor=black&color=C9FF30)](osmfeula.txt)
[![OSS](https://img.shields.io/github/license/devlooped/Agents.AI.svg?color=blue)](license.txt) 

Extensions for Microsoft.Agents.AI

<!-- include https://github.com/devlooped/.github/raw/main/osmf.md -->
## Open Source Maintenance Fee

To ensure the long-term sustainability of this project, users of this package who generate 
revenue must pay an [Open Source Maintenance Fee](https://opensourcemaintenancefee.org). 
While the source code is freely available under the terms of the [License](license.txt), 
this package and other aspects of the project require [adherence to the Maintenance Fee](osmfeula.txt).

To pay the Maintenance Fee, [become a Sponsor](https://github.com/sponsors/devlooped) at the proper 
OSMF tier. A single fee covers all of [Devlooped packages](https://www.nuget.org/profiles/Devlooped).

<!-- https://github.com/devlooped/.github/raw/main/osmf.md -->

<!-- #content -->
## Overview

Microsoft.Agents.AI (aka [Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/agent-framework-overview) 
is a comprehensive API for building AI agents. Its programatic model (which follows closely 
the [Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai) 
approach) provides maximum flexibility with little prescriptive structure.

This package provides additional extensions to make developing agents easier and more 
declarative.

## Configurable Agents

Tweaking agent options such as description, instructions, chat client to use and its 
options, etc. is very common during development/testing. This package provides the ability to 
drive those settings from configuration (with auto-reload support). This makes it far easier 
to experiment with various combinations of agent instructions, chat client providers and 
options, and model parameters without changing code, recompiling or even restarting the application:

> [!NOTE]
> This example shows integration with configurable chat clients feature from the 
> Devlooped.Extensions.AI package, but any `IChatClient` registered in the DI container 
> with a matching key can be used.

```json
{
  "AI": {
    "Agents": {
      "MyAgent": {
        "Description": "An AI agent that helps with customer support.",
        "Instructions": "You are a helpful assistant for customer support.",
        "Client": "Grok",
        "Options": {
          "ModelId": "grok-4",
          "Temperature": 0.5,
        }
      }
    },
    "Clients": {
      "Grok": {
        "Endpoint": "https://api.grok.ai/v1",
        "ModelId": "grok-4-fast-non-reasoning",
        "ApiKey": "xai-asdf"
      }
    }
  }
}
````

```csharp
var host = new HostApplicationBuilder(args);
host.Configuration.AddJsonFile("appsettings.json, optional: false, reloadOnChange: true);

// 👇 implicitly calls AddChatClients
host.AddAIAgents(); 

var app = host.Build();
var agent = app.Services.GetRequiredKeyedService<AIAgent>("MyAgent");
```

> [!NOTE]
> The configurable chat clients functionality is provided by the 
> [Devlooped.Extensions.AI](https://www.nuget.org/packages/Devlooped.Extensions.AI) dependency


You can of course use any config format supported by .NET configuration, such as 
TOML which is arguably more human-friendly for hand-editing:

```toml
[ai.clients.openai]
modelid = "gpt-4.1"

[ai.clients.grok]
endpoint = "https://api.x.ai/v1"
modelid = "grok-4-fast-non-reasoning"

[ai.agents.orders]
description = "Manage orders using catalogs for food or any other item."
instructions = """
        You are an AI agent responsible for processing orders for food or other items.
        Your primary goals are to identify user intent, extract or request provider information, manage order data using tools and friendly responses to guide users through the ordering process.
    """

# ai.clients.openai, can omit the ai.clients prefix
client = "openai"

[ai.agents.orders.options]
modelid = "gpt-4o-mini"
```

This can be used by leveraging [Tomlyn.Extensions.Configuration](https://www.nuget.org/packages/Tomlyn.Extensions.Configuration).

> [!NOTE]
> This package will automatically dedent and trim start and end newlines from 
> multi-line instructions and descriptions when applying the configuration, 
> avoiding unnecessary tokens being used for indentation while allowing flexible 
> formatting in the config file.

You can also leverage the format pioneered by [VS Code Chat Modes](https://code.visualstudio.com/docs/copilot/customization/custom-chat-modes), 
 (or "custom agents") by using markdown format plus YAML front-matter for better readability:

```yaml
---
id: ai.agents.notes
description: Provides free-form memory
client: grok
model: grok-4-fast
---
You organize and keep notes for the user.
# Some header
More content
```

Visual Studio Code will ignore the additional attributes used by this project. In particular, the `model` 
property is a shorthand for setting the `options.modelid`, but in our implementation, the latter takes 
precedence over the former, which allows you to rely on `model` to drive the VSCode testing, and the 
longer form for run-time with the Agents Framework: 

```yaml
---
id: ai.agents.notes
description: Provides free-form memory
model: Grok Code Fast 1 (copilot)
client: grok
options: 
  modelid: grok-code-fast-1
---
// Instructions
```

![agent model picker](assets/img/agent-model.png)

Use the provided `AddAgentMarkdown` extension method to load instructions from markdown files as follows:

```csharp
var host = new HostApplicationBuilder(args);
host.Configuration.AddAgentMarkdown("notes.agent.md", optional: false, reloadOnChange: true);
```

The `id` field in the front-matter is required and specifies the configuration section name, and 
all other fields are added as if they were specified under it in the configuration.

### Extensible AI Contexts

The Microsoft [agent framework](https://learn.microsoft.com/en-us/agent-framework/overview/agent-framework-overview) allows extending 
agents with dynamic context via [AIContextProvider](https://learn.microsoft.com/en-us/dotnet/api/microsoft.agents.ai.aicontextprovider) 
and `AIContext`. This package supports dynamic extension of a configured agent in the following ways (in order of priority): 

1. Keyed services `AIContext` with the same name as the agent.
2. Keyed services `AIContextProvider` with the same name as the agent.
3. Other services pulled in via `use` setting for an agent registered as either `AIContextProvider` or `AIContext`
   with a matching key.

For example, let's say you want to provide consistent tone for all your agents. It would be tedious, repetitive and harder 
to maintain if you had to set that in each agent's instructions. Instead, you can define a reusable context named `tone` such as:

```toml
[ai.context.tone]
instructions = """\
    Default to using spanish language, using argentinean "voseo" in your responses \
    (unless the user explicitly talks in a different language). \
    This means using "vos" instead of "tú" and conjugating verbs accordingly. \
    Don't use the expression "pa'" instead of "para". Don't mention the word "voseo".
    """
```

Then, you can reference that context in any agent using the `use` setting:
```toml
[ai.agents.support]
description = "An AI agent that helps with customer support."
instructions = "..."
client = "grok"
use = ["tone"]

[ai.agents.sales]
description = "An AI agent that helps with sales inquiries."
instructions = "..."
client = "openai"
use = ["tone"]
```

Configured contexts can provide all three components of an `AIContext`: instructions, messages and tools, such as:

```toml
[ai.context.timezone]
instructions = "Always assume the user's timezone is America/Argentina/Buenos_Aires unless specified otherwise."
messages = [
    { system = "You are aware of the current date and time in America/Argentina/Buenos_Aires." }
]
tools = ["get_date"]
```

If multiple contexts are specified in `use`, they are added in order.

In addition to configured sections, the `use` property can also reference exported contexts as either `AIContext` 
(for static context) or `AIContextProvider` (for dynamic context) registered in DI with a matching name.


### Extensible Tools

The `tools` section allows specifying tool names registered in the DI container, such as:

```csharp
services.AddKeyedSingleton("get_date", AIFunctionFactory.Create(() => DateTimeOffset.Now, "get_date"));
```

This tool will be automatically wired into any agent that uses the `timezone` context above.

Agents themselves can also add tools from DI into an agent's context without having to define an entire 
section just for that, by specifying the tool name directly in the `tools` array:

```toml
[ai.agents.support]
description = "An AI agent that helps with customer support."
instructions = "..."
client = "grok"
use = ["tone"]
tools = ["get_date"]
```

This enables a flexible and convenient mix of static and dynamic context for agents, all driven 
from configuration.

In addition to registering your own tools in DI, you can also leverage the MCP C# SDK and reuse 
the same tool declarations: 

```csharp
builder.Services.AddMcpServer().WithTools<NotesTools>();

// 👇 Reuse same tool definitions in agents
builder.AddAIAgents().WithTools<NotesTools>();
```

### Agent Skills

Agents can be equipped with [Agent Skills](https://learn.microsoft.com/en-us/agent-framework/agents/skills) 
— portable, file-based packages of instructions and resources that give agents specialised capabilities. 
To enable file-based skills for an agent, add a `skills` array to its configuration section:

```toml
[ai.agents.support]
description = "An AI agent that helps with customer support."
instructions = "..."
client = "grok"
skills = ["*"]
```

The `"*"` entry is a wildcard that instructs the agent to discover all skills located in the 
`skills/` folder relative to the application's base directory (`AppContext.BaseDirectory/skills`).

Each skill is a subdirectory containing a `SKILL.md` file (see the 
[Agent Skills specification](https://agentskills.io/) for the full format). 
The `FileAgentSkillsProvider` searches up to two directory levels deep, so you can organise your 
skills in sub-folders:

```
skills/
├── expense-report/
│   └── SKILL.md
└── order-tracking/
    └── SKILL.md
```

The skills provider implements the progressive-disclosure pattern: skill names and descriptions are 
injected into the agent's system prompt, and the full instructions are only loaded when the agent 
decides to use a skill via the `load_skill` tool.

<!-- #content -->

<!-- include https://github.com/devlooped/sponsors/raw/main/footer.md -->
# Sponsors 

<!-- sponsors.md -->
[![Clarius Org](https://avatars.githubusercontent.com/u/71888636?v=4&s=39 "Clarius Org")](https://github.com/clarius)
[![MFB Technologies, Inc.](https://avatars.githubusercontent.com/u/87181630?v=4&s=39 "MFB Technologies, Inc.")](https://github.com/MFB-Technologies-Inc)
[![SandRock](https://avatars.githubusercontent.com/u/321868?u=99e50a714276c43ae820632f1da88cb71632ec97&v=4&s=39 "SandRock")](https://github.com/sandrock)
[![DRIVE.NET, Inc.](https://avatars.githubusercontent.com/u/15047123?v=4&s=39 "DRIVE.NET, Inc.")](https://github.com/drivenet)
[![Keith Pickford](https://avatars.githubusercontent.com/u/16598898?u=64416b80caf7092a885f60bb31612270bffc9598&v=4&s=39 "Keith Pickford")](https://github.com/Keflon)
[![Thomas Bolon](https://avatars.githubusercontent.com/u/127185?u=7f50babfc888675e37feb80851a4e9708f573386&v=4&s=39 "Thomas Bolon")](https://github.com/tbolon)
[![Kori Francis](https://avatars.githubusercontent.com/u/67574?u=3991fb983e1c399edf39aebc00a9f9cd425703bd&v=4&s=39 "Kori Francis")](https://github.com/kfrancis)
[![Uno Platform](https://avatars.githubusercontent.com/u/52228309?v=4&s=39 "Uno Platform")](https://github.com/unoplatform)
[![Reuben Swartz](https://avatars.githubusercontent.com/u/724704?u=2076fe336f9f6ad678009f1595cbea434b0c5a41&v=4&s=39 "Reuben Swartz")](https://github.com/rbnswartz)
[![Jacob Foshee](https://avatars.githubusercontent.com/u/480334?v=4&s=39 "Jacob Foshee")](https://github.com/jfoshee)
[![](https://avatars.githubusercontent.com/u/33566379?u=bf62e2b46435a267fa246a64537870fd2449410f&v=4&s=39 "")](https://github.com/Mrxx99)
[![Eric Johnson](https://avatars.githubusercontent.com/u/26369281?u=41b560c2bc493149b32d384b960e0948c78767ab&v=4&s=39 "Eric Johnson")](https://github.com/eajhnsn1)
[![Jonathan ](https://avatars.githubusercontent.com/u/5510103?u=98dcfbef3f32de629d30f1f418a095bf09e14891&v=4&s=39 "Jonathan ")](https://github.com/Jonathan-Hickey)
[![Ken Bonny](https://avatars.githubusercontent.com/u/6417376?u=569af445b6f387917029ffb5129e9cf9f6f68421&v=4&s=39 "Ken Bonny")](https://github.com/KenBonny)
[![Simon Cropp](https://avatars.githubusercontent.com/u/122666?v=4&s=39 "Simon Cropp")](https://github.com/SimonCropp)
[![agileworks-eu](https://avatars.githubusercontent.com/u/5989304?v=4&s=39 "agileworks-eu")](https://github.com/agileworks-eu)
[![Zheyu Shen](https://avatars.githubusercontent.com/u/4067473?v=4&s=39 "Zheyu Shen")](https://github.com/arsdragonfly)
[![Vezel](https://avatars.githubusercontent.com/u/87844133?v=4&s=39 "Vezel")](https://github.com/vezel-dev)
[![ChilliCream](https://avatars.githubusercontent.com/u/16239022?v=4&s=39 "ChilliCream")](https://github.com/ChilliCream)
[![4OTC](https://avatars.githubusercontent.com/u/68428092?v=4&s=39 "4OTC")](https://github.com/4OTC)
[![domischell](https://avatars.githubusercontent.com/u/66068846?u=0a5c5e2e7d90f15ea657bc660f175605935c5bea&v=4&s=39 "domischell")](https://github.com/DominicSchell)
[![Adrian Alonso](https://avatars.githubusercontent.com/u/2027083?u=129cf516d99f5cb2fd0f4a0787a069f3446b7522&v=4&s=39 "Adrian Alonso")](https://github.com/adalon)
[![Michael Hagedorn](https://avatars.githubusercontent.com/u/61711586?u=8f653dfcb641e8c18cc5f78692ebc6bb3a0c92be&v=4&s=39 "Michael Hagedorn")](https://github.com/Eule02)
[![torutek](https://avatars.githubusercontent.com/u/33917059?v=4&s=39 "torutek")](https://github.com/torutek)
[![mccaffers](https://avatars.githubusercontent.com/u/16667079?u=f5b761303b6c7a7f18123b5bd20f06760d3fbd3e&v=4&s=39 "mccaffers")](https://github.com/mccaffers)
[![Seika Logiciel](https://avatars.githubusercontent.com/u/2564602?v=4&s=39 "Seika Logiciel")](https://github.com/SeikaLogiciel)
[![Andrew Grant](https://avatars.githubusercontent.com/devlooped-user?s=39 "Andrew Grant")](https://github.com/wizardness)
[![Lars](https://avatars.githubusercontent.com/u/1727124?v=4&s=39 "Lars")](https://github.com/latonz)


<!-- sponsors.md -->
[![Sponsor this project](https://avatars.githubusercontent.com/devlooped-sponsor?s=118 "Sponsor this project")](https://github.com/sponsors/devlooped)

[Learn more about GitHub Sponsors](https://github.com/sponsors)

<!-- https://github.com/devlooped/sponsors/raw/main/footer.md -->
