(*
   Copyright 2015-2016 Philip Curzon

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*)

namespace NovelFS.NovelIO

open NovelFS.FSPipes
open Pipes.Operators

module Consumer =
    /// A consumer that writes out data to the processes' stdout stream
    let stdOutLine : Consumer<string, unit> = Pipes.for' Pipes.identity (Pipes.liftAsync << async.Return << System.Console.WriteLine)

    /// Creates a consumer that writes data out to a supplied file
    let writeToFile file : Consumer<_,_> =
        pipe {
            use stream = new System.IO.FileStream(file, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None, 4096, true)
            return! Pipes.for' Pipes.identity (Pipes.liftAsync << stream.AsyncWrite)
        }

    /// Creates a consumer that writes data out to a supplied file line by line
    let writeLinesToFile file : Consumer<_,_> =
        pipe {
            use stream = new System.IO.FileStream(file, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None, 4096, true)
            use streamWriter = new System.IO.StreamWriter(stream)
            return! Pipes.for' Pipes.identity (fun (str : string) -> Pipes.liftAsync << Async.AwaitTask <| (streamWriter.WriteLineAsync(str).ContinueWith<unit>(fun _ -> ())))
        }
        
         



