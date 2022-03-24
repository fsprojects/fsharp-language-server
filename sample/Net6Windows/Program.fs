// For more information see https://aka.ms/fsharp-console-apps
open Elmish.WPF
type Model =
    { Count: int
      StepSize: int }

let init () =
    { Count = 0
      StepSize = 1 }
type Msg =
    | Increment
    | Decrement
    | SetStepSize of int
let update msg m =
    match msg with
        | Increment -> { m with Count = m.Count + m.StepSize }
        | Decrement -> { m with Count = m.Count - m.StepSize }
        | SetStepSize x -> { m with StepSize = x }

let bindings () =
    [
        "CounterValue" |> Binding.oneWay (fun m -> m.Count)
        "Increment" |> Binding.cmd (fun _ -> Increment)
        "Decrement" |> Binding.cmd (fun _ -> Decrement)
        "StepSize" |> Binding.twoWay(
        (fun m -> float m.StepSize),
        (fun newVal _ -> int newVal |> SetStepSize))
    ]

let main window =
    WpfProgram.mkSimple init update bindings
    |> WpfProgram.startElmishLoop window