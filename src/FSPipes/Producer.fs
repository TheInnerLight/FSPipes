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

open Pipes.Operators

module StreamHelper =
    let asyncRead count (stream : System.IO.Stream) =
        let buffer = Array.zeroCreate count
        async {
            let! n = stream.AsyncRead(buffer,0,count)
            return Array.take n buffer
            }
        

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Producer =

    /// Advances the producer getting a choice of either the final value or a value and the producer to generate the subsequent value
    let next (p : Producer<'bout, 'v>) : Async<Choice<'v, 'bout * Producer<'bout,'v>>> = 
        let rec nextRec p = 
            match p with
            |Request (_) -> invalidOp "Impossible"
            |Respond (a, fu) -> async.Return (Choice2Of2 (a, fu ()))
            |AsyncM m  -> async.Bind(m, nextRec)
            |Value r -> async.Return (Choice1Of2 r)
        nextRec p

    /// Folds over a producer using a supplied accumulation function, initial accumulator value and producer.
    /// (Note: this function is not an idiomatic use of Pipes but it is included to facilitate the development of unit tests.)
    let fold f acc (p0 : Producer<_, _>) =
        let rec foldRec p x =
            match p with
            |Request (_)  -> invalidOp "Impossible"
            |Respond (a, fu) -> foldRec (fu ()) (f x a)
            |AsyncM m  -> async.Bind(m, (fun p' -> foldRec p' x))
            |Value _ -> async.Return x
        foldRec p0 acc

    /// Create a producer from a supplied file which supplies the bytes that make up the file as a series of byte arrays
    let fromFile path : Producer<_,_> =
        let stream = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read, 4096, true)
        let read = StreamHelper.asyncRead 4096 stream
        let rec fromFileRec() =
            pipe {
                let! x = Pipes.liftAsync read
                match x with
                |[||] -> 
                    stream.Close()
                    return ()
                |_ ->
                    do! Pipes.yield' x
                    return! fromFileRec()
            }
        fromFileRec()
        
    /// Create a producer which delivers the data from a supplied sequence
    let fromSeq seq : Producer<_,_> = Pipes.each seq

    /// Create a producer from an initial state and generation function
    let rec unfold generator state : Producer<_,_> =
        pipe {
            match generator state with
            |Choice1Of2 v -> return v
            |Choice2Of2 (acc, state') ->
                do! Pipes.yield' acc
                return! unfold generator state'
            }

    /// A producer which delivers data from the processes' stdin stream
    let stdInLine : Producer<_, _> =
        Pipeline.forever (
            let pl' = Pipes.liftAsync << async.Return <| System.Console.ReadLine()
            pl' >>= Pipes.yield')
        
