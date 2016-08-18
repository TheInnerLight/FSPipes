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
    /// Create a producer from a supplied file which supplies the bytes that make up the file as a series of byte arrays
    let fromFile path : Producer<_,_> =
        Pipes.pipe  {
            let! channel = Pipes.liftIO <| File.openBinaryChannel FileMode.Open FileAccess.Read (Filename.CreateFromString path)
            // loop over the channel, producing more data until the source is exhausted
            let rec loop() =
                Pipes.pipe {
                    let! neof = Pipes.liftIO <| BinaryChannel.isReady channel
                    match neof with
                    |true ->
                        let! read = Pipes.liftIO <| BinaryChannel.readBytes channel 1024
                        do! Pipes.yield' read
                        do! loop()
                    |false -> return ()
                }
            return! loop()
            }
        
    /// Create a producer which delivers the data from a supplied sequence
    let fromSeq seq = Pipes.each seq

    /// A producer which delivers data from the processes' stdin stream
    let stdInLine : Producer<_, _> =
        Pipeline.forever (
            let pl' = Pipes.liftIO (Console.readLine)
            Pipeline.bind pl' (Pipes.yield'))
        
