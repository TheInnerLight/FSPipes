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

namespace NovelFS.NovelIO.UnitTests

open NovelFS.NovelIO
open NovelFS.NovelIO.BinaryPickler
open NovelFS.FSPipes
open Pipes.Operators
open FsCheck
open FsCheck.Xunit

type ``Pipes Unit Tests``() =
    [<Property>]
    static member ``map on pure sequence transforms each element by mapping function`` (values : int list) =
        let mapped = Pipes.each values >-> Pipes.map ((+) 1)
        let list = 
            Pipes.fold (fun acc v ->acc @ [v]) [] mapped
            |> Pipes.runEffect 
            |> IO.run
        list = List.map ((+) 1) values

    [<Property>]
    static member ``filter on pure sequence removes elements that do not satisfy predicate`` (values : int list) =
        let mapped = Pipes.each values >-> Pipes.filter (fun x -> x > 5)
        let list = 
            Pipes.fold (fun acc v ->acc @ [v]) [] mapped
            |> Pipes.runEffect 
            |> IO.run
        list = List.filter (fun x -> x > 5) values

