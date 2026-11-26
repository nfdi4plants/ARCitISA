## Git Rules

- You may commit changes locally, but you MUST NOT push them to the remote repository. You MUST never add yourself as a collaborator or make any changes that affect the remote repository.
- You may resolve merge issues when the user requests it, but you MUST NOT push the resolved changes to the remote repository.

## ReactComponent design rules

- **Purpose**: These guidelines are for components in `src/Components`, which are meant to be reusable and application-agnostic. They should not contain app-specific logic or state coupling.
- Read the full guidelines in [docs/ReactComponentDesign.md](docs/ReactComponentDesign.md) for detailed rules on file structure, syntax, and design principles for components in `src/Components`.

## Icon Convention rules

- Use `swt:iconify` with fully qualified icon classes (prefer `swt:fluent--...`) for new icons.

## Test Convention rules

- You MUST NOT do source file testing to check if certain code is present. This is a brittle testing strategy that will break if the code is refactored, even if the behavior is unchanged. Instead, test the behavior directly by calling the relevant functions and checking their outputs or side effects.
- Showcases MUST be implemented as native Storybook `.stories.tsx` files.
- Unit tests MUST be implemented in `.test.fs` files.
- React Testing Library tests MUST be implemented in `.test.rtl.fs` files.

## Fable Convention rules

Fable is used to transpile F# code to JavaScript/TypeScript. Never directly write to an `.fs.{ts,js,tsx}` file because it will be overwritten by the transpiler. Instead, write to the corresponding `.fs` file and let Fable handle the transpilation. You MAY inspect the transpiled code to understand how F# constructs are translated to JavaScript, but you MUST NOT edit the transpiled code directly.

- You MUST implement reusable bindings for JavaScript native libraries/functions. Dynamic Access using `?` can be brittle and SHOULD be avoided in favor of strongly typed bindings.
- Not all F# types translate to native JavaScript types. This can be important in certain performance-sensitive scenarios as well as outward facing APIs, which are meant to be usable from JavaScript apps. (The transpiled Arcitisa.Components project is also published to npm.)
    - Anonymous record types and classes with the `[<POJO>]` attribute translate to native JavaScript objects.
    - Arrays/ResizeArrays translate to native JavaScript arrays.
    - for promised use Fable.Promise's `promise {}` computation expression. It can be converted easily from and to F#s `async {}` computation expression.
    - `[<StringEnum>]` discriminated unions translate to native JavaScript strings.
    - `[<Erase>]`/`U2`/`U3`/.. discriminated unions translate to JavaScript fields, which allow for different types to be used in the same field. The transpiler will erase the union type and only keep the underlying types, which can be used in JavaScript code.
    - fetches can be done using Fable.Fetch (https://github.com/fable-compiler/fable-fetch)

        ```fsharp
        type IUser =
            abstract name: string

        let fetchGitHubUser accessToken =
            async {
                let! response =
                fetch "https://api.github.com/user" [
                    requestHeaders [
                        HttpRequestHeaders.Authorization $"token {accessToken}"
                    ] ] |> Async.AwaitPromise
                let! item = response.json<IUser>() |> Async.AwaitPromise
            }
        ```
