# Pull requests

Eight pull requests from LanguZe's repository, as they were merged. Each one is a single change with its own verification, and from the port on, each also says what the checks caught, including mistakes made along the way.

## Building Release 1.0

The NestJS API and the Next.js web app, from the first AI adapter to running in production.

| Pull request                                                              | Why read it                                                                                                                                                                                                          |
| ------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [#42 The Gemini adapter](pull-request-42.md)                              | The first adapter behind the AI interface: structured output, boxes turned from Gemini's scale into LanguZe's, and why Gemini refusing the safety check is a block while refusing the extraction is a provider error |
| [#43 The analysis pages](pull-request-43.md)                              | The web side of an analysis: polling while it runs, a failure that says whether it used one of the day's analyses, and words beside numbered boxes that never rely on colour                                         |
| [#71 Send Gemini's turn back whole after a tool call](pull-request-71.md) | The AI evaluation catching a real bug: every tutor reply that used a tool failed, because Gemini's thought signature was lost on the way back                                                                        |
| [#74 Choose the AI models with the evaluation](pull-request-74.md)        | Choosing the models from measured results, 98.8% correct words, rather than by assumption                                                                                                                            |
| [#87 Let the database sleep while nothing needs it](pull-request-87.md)   | A production problem: a scheduled task kept the serverless database awake, which would have used up its free hours in the middle of every month                                                                      |

## Porting to ASP.NET Core

The API ported slice by slice, with the NestJS API as the reference and black-box contract tests as the judge ([ADR-0006](../code/docs/architecture/adr/0006-aspnet-core-api.md)).

| Pull request                                                                                  | Why read it                                                                                                                               |
| --------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| [#102 The first black-box contract tests](pull-request-102.md)                                | The suite that runs unchanged against both APIs, written and passing against NestJS before any .NET code existed                          |
| [#129 Blocked photos, the pauses they earn and the stale analysis check](pull-request-129.md) | A slice with transactions, a job and email after commit, checked with ten deliberate mutations; #87's sleeping database, carried into C#  |
| [#130 The Gemini adapter for photo analysis](pull-request-130.md)                             | #42 in C#: comparing the prompts word for word found a missing sentence, and two SDK defaults had to be set to match the JavaScript SDK's |
