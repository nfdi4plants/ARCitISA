# Guidelines for isolated React components (`src/Components`)

Components in `src/Components` are expected to be reusable and application-agnostic.

# File Rules

- **Primitive, Composite, Page Level**: Components should be placed in the appropriate folder based on their complexity and reusability.
    - `Primitive`: Components that are simple and reusable across multiple applications. They should have no dependencies on other components in `src/Components`.
    - `Composite`: Components that are composed of multiple primitive components. They may have dependencies on other components in `src/Components`.
    - `Page`: Components that are specific to a single application and may have dependencies on other components in `src/Components`. These components are designed for a specific purpose and should be includable directly into a page or view of an application. Of course the components can still require some input argument, such as data or fetch calls.
- **Component folder**: Each component should be placed in a folder named after the component and sorted into the appropriate level folder (`Primitive`, `Composite`, `Page`).
    - e.g. `src/Components/src/Composite/TermSearch` contains `TermSearch.tsx`, `TermSearchConfigProvider.fs` and `TermSearchConfigSetter.fs`
    - e.g. `src/Components/src/Composite/Authentication` contains `Authentication.fs`, `AccountManager.fs`, `Helper.fs` and `Types.fs`
    - e.g. higher level nested components MAY be placed in subfolders and add the subfolder name to the namespace, e.g. `Composite/Notes/NoteSearch/NoteSearchComponent.fs` with namespace `namespace Swate.Components.Composite.Notes.NoteSearch`.
- **PascalCase file names**: `MyComponent.tsx` for the component "MyComponent".
- **File namespace**: Component files MUST have a namespace that follows the folder structure and use a `type <FileName> =` declaration. Non-component files MUST follow folder structure down to the file and use module declaration.

    For example:

    -  `src/Components/src/Composite/Authentication/Authentication.fs` is a component file and should have:

        ```fsharp
        namespace Swate.Components.Composite.Authentication

        [<Erase; Mangle(false)>]
        type Authentication =
            // ...
        ```

    - `src/Components/src/Composite/Authentication/Types.fs` is NOT a component file and should have:

        ```fsharp
        module Swate.Components.Composite.Authentication.Types

        // ...
        ```
- **Colocated stories**: 
    - Storybook files must be colocated with the component and named `<Component>.stories.tsx`.
    - JavaScript and TypeScript files MUST NOT import `fable-library` directly. Put F# runtime operations in F# helpers (for example, a colocated `.sample.fs` file) and import the generated wrapper functions instead. This also avoids coupling stories to versioned `fable_modules` paths.
    - Files with sample data for stories must be named `<Component>.sample.fs` and colocated with the component. (`sample` is used to place the file between the main file (`.fs`) and the `.stories` file in the alphabetic order)
        - This file should use the namespace `Swate.Components.<level>.<Component>Sample` and be marked as `internal`.
- **Types**
    - Types used by multiple files MUST be placed in a separate file `Types.fs`. 
    - Types only used inside a single file can MUST be placed inside a private `module <FileName>Types` in the same file. 
    - If there are multiple domains requiring types, place them in a `Types/<DomainName>.fs` file.
- **Helper functions**: Helper functions follow the same rules as Types.
- **Subcomponents**: 
    - Large subcomponents SHOULD be split into logical subcomponents.
    - Subcomponents MUST be defined as static members of the main component class if they are sufficiently simple.
    - Subcomponents MUST be placed in separate component-files in the same folder if they are complex.

# Syntax Rules

- **PascalCase**: MUST use PascalCase for **component** and **object** names.
- **CamelCase**: MUST use camelCase for **props** and **functions**.
- **`[<ReactComponent>]`**: If a component returns a `ReactElement`, it MUST be decorated with `[<ReactComponent>]`.
- **Components as static members**: Any components MUST be defined as static members of a class.
    - They MUST use tupled args. This allows us to use optional params with `?optionalParam` syntax, as well as named params with `namedParam = namedParamValue` syntax.
    - These Classes MUST have the `[<Erase; Mangle(false)>]` attribute to improve interop with native TypeScript.
    - Public component names MUST be Standalone descriptive if they are intended for TypeScript interop. The main component name MUST be equal to the file name. Subcomponent names MUST be descriptive of their purpose and not include the main component name as a prefix. For example, if the main component is `MyComponent`, a subcomponent should be named `Header` instead of `MyComponentHeader`. In addition, subcomponents SHOULD be private.

    ```fsharp
    [<Erase; Mangle(false)>]
    type MyComponent =
        [<ReactComponent>]
        static member MyComponent () : ReactElement =
            // ...
    ```
- **React context**: If a component needs to share state or configuration with a React context. It MUST define the base context in a separate file `**/<ComponentName>/Context.fs` or `**/<ComponentName>/Contexts/<ContextName>Context.fs` or `**/<ComponentName>/<ContextName>Context.fs` if there are multiple contexts associated with the component, with the following properties.
    - File namespace MUST follow folder structure and be named `module Swate.Components.<Path>.<ContextName>Context` or if there is a single context `module Swate.Components.<Path>.Context`.
    - MAY contain the relevant public types.
    - MAY contain helper functions inside a MAYBE public module. This module should be named `<ContextName>ContextHelper`.
    - It MUST NOT contain any React components.
    - It MUST contain the `React.createContext` implementation in PascalCase with the suffix `Ctx`. For example, `ExampleCtx`.
        - It must be a root level `let` binding.
    - It MUST contain a hook to easily bind to the context. This hook MUST be named `use<ContextName>Ctx`. For example, `useExampleCtx`.
    - Example for an context "Example" for a component "ExampleComponent":

        ```fsharp
        module Swate.Components.ExampleComponent.Context

        // ..

        type PublicBananaType = {
            // ...
        }

        module ExampleContextHelper =
            // ...

        let ExampleCtx = React.createContext<PublicBananaType> (PublicBananaType.init ())

        [<Hook>]
        let useExampleCtx () = React.useContext ExampleCtx
        ```

# Design Rules

- Keep states at the lowest level possible. Every state change forces a re-render of the component and all its children. If a state is only used in a subcomponent, it should be defined in that subcomponent. 
- Only use "useEffect" when absolutly necessary. It is a common source of bugs and should be avoided if possible. Check out this read: https://react.dev/learn/you-might-not-need-an-effect

# Zen

> The GitHub API consolidates the Zen of GitHub in its own codebase, in 14 aphorisms:

- Responsive is better than fast
- It’s not fully shipped until it’s fast
- Anything added dilutes everything else
- Practicality beats purity
- Approachable is better than simple
- Mind your words, they are important
- Speak like a human
- Half measures are as bad as nothing at all
- Encourage flow
- Non-blocking is better than blocking
- Favor focus over features
- Avoid administrative distraction
- Design for failure
- Keep it logically awesome
