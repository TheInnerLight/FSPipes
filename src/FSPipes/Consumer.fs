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
    let stdOutLine : Consumer<_,_> = Pipes.for' Pipes.identity (Pipes.liftIO << Console.writeLine)

    /// Creates a consumer from a supplied binary channel
    let fromChannel chan : Consumer<_,_>  = Pipes.for' Pipes.identity (Pipes.liftIO << BinaryChannel.writeBytes chan)

    let fromTChannel chan : Consumer<_,_>  = Pipes.for' Pipes.identity (Pipes.liftIO << TextChannel.putStrLn chan)

    /// Creates a consumer that writes data out to a supplied file
    let writeToFile file : Consumer<_,_> =
        File.withBinaryChannel File.Open.defaultWrite file (IO.return' << fromChannel)
        |> Pipes.liftIO
        |> Pipeline.join

    let writeLinesToFile file : Consumer<_,_> =
        let channel = File.openTextChannel File.Open.defaultWrite file
        let x = 
            IO.map (fromTChannel) channel
            |> Pipes.liftIO
            |> Pipeline.join
        let close = Pipes.liftIO <| IO.bind channel (TextChannel.close)
        pipe {
            do! x
            do! close
            return! x
        }
        
         



