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

namespace NovelFS.FSPipes

open NovelFS.NovelIO

module Producer =
    let fromFile path =
        //use stream = new System.IO.FileStream (path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read)
        Pipes.pipe  {
            let! channel = Pipes.lift <| File.openBinaryChannel FileMode.Open FileAccess.Read (Filename.CreateFromString path)
            let rec loop() =
                Pipes.pipe {
                    let! neof = Pipes.lift <| BinaryChannel.isReady channel
                    match neof with
                    |true ->
                        let! read = Pipes.lift <| BinaryChannel.readBytes channel 1024
                        do! Pipes.yield' read
                        do! loop()
                    |false -> return ()
                }
            return! loop()
            }
        

    let fromSeq seq = Pipes.each seq

    let stdInLine =
        Pipeline.forever (
            let pl' = Pipes.lift (Console.readLine)
            Pipeline.bind pl' (Pipes.yield'))
        
