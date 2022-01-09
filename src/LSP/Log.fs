module LSP.Log

open Serilog
open Microsoft.FSharp.Core.Printf
open System
open System.Collections.Generic
open System.IO
open Serilog
open Serilog
open Serilog.Core
open Serilog.Events
let diagnosticsLog = ref stderr
/// Print to LSP.Log.diagnosticsLog, which is stderr by default but can be redirected
let dprintfn(fmt: Printf.TextWriterFormat<'T>): 'T = 
    Printf.fprintfn diagnosticsLog.Value fmt


let lgInfof fmt=
    ksprintf (Log.Information )fmt
let lgWarnf fmt=
    ksprintf (Log.Warning )fmt
let lgErrorf fmt=
    ksprintf (Log.Error )fmt
let lgVerbosef fmt=
    ksprintf (Log.Verbose )fmt
let lgDebugf fmt=
    ksprintf (Log.Debug )fmt 

let lgInfo  (message:string) (data:obj)=
    Log.Information(message, data)
let lgInfo2  (message:string) (data:obj) (data2:obj)=
    Log.Information(message, data,data2)
let lgInfo3  (message:string) (data:obj) (data2:obj) (data3:obj)=
    Log.Information(message, data,data2,data3)
            
let lgError  (message:string) (data:obj)=
    Log.Error(message, data)
let lgError2  (message:string) ( data:obj ) ( data2:obj )=
    Log.Error(message, data,data2)
let lgError3  (message:string) (data:obj) ( data2:obj ) (data3:obj)=
    Log.Error(message, data,data2,data3)
   
let lgWarn  (message:string) (data:obj)=
    Log.Warning(message, data)
let lgWarn2  (message:string) (data:obj) (data2:obj)=
    Log.Warning(message, data,data2)
let lgWarn3  (message:string) (data:obj) (data2:obj) (data3:obj)=
    Log.Warning(message, data,data2,data3)

let lgDebug  (message:string) (data:obj)=
    Log.Debug(message, data)
let lgDebug2  (message:string) (data:obj) (data2:obj)=
    Log.Debug(message, data,data2)
let lgDebug3  (message:string) (data:obj) (data2:obj) (data3:obj)=
    Log.Debug(message, data,data2,data3)
    
let lgVerb  (message:string) (data:obj)=
    Log.Verbose(message, data)
let lgVerb2  (message:string) (data:obj) (data2:obj)=
    Log.Verbose(message, data,data2)
let lgVerb3  (message:string) (data:obj) (data2:obj) (data3:obj)=
    Log.Verbose(message, data,data2,data3)




let startTime=DateTime.Now
let createLogger logPath =
    let logName=sprintf "%sdebugLog-%i-%i_%i;%i-%is--.log" logPath startTime.Month startTime.Day startTime.Hour startTime.Minute startTime.Second
    dprintfn "%s"logName
    
    let logger=
        Serilog.LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.File(logName ,Serilog.Events.LogEventLevel.Verbose,rollingInterval=RollingInterval.Day,fileSizeLimitBytes=(int64 (1000*1000)))
            .WriteTo.File(logPath+"simpleLog-.log",Serilog.Events.LogEventLevel.Information,rollingInterval=RollingInterval.Day,fileSizeLimitBytes=(int64 (1000*1000)))
            .WriteTo.Console(theme=Sinks.SystemConsole.Themes.SystemConsoleTheme.Literate,restrictedToMinimumLevel=Serilog.Events.LogEventLevel.Information, standardErrorFromLevel=Serilog.Events.LogEventLevel.Information)
            .CreateLogger()
    (* let logger2=
        Serilog.LoggerConfiguration()  
            .WriteTo.Async(
                fun c -> c.Console(outputTemplate = outputTemplate, standardErrorFromLevel = Nullable<_>(LogEventLevel.Verbose), theme = Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code) |> ignore
            ) // make it so that every console log is logged to stderr
 *)
    Serilog.Log.Logger<-logger
    dprintfn "logger created"
