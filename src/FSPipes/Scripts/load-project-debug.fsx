// Warning: generated file; your changes could be lost when a new file is generated.
#I __SOURCE_DIRECTORY__
#load "load-references-debug.fsx"
#load "../PipeInternal.fs"
      "../Encoding.fs"
      "../Pipes.fs"
      "../Producer.fs"
      "../Consumer.fs"

open NovelFS.FSPipes
open NovelFS.FSPipes.Pipes.Operators

let effect = Producer.fromFile """dd_vs2015.3_decompression_log.txt""" >-> Pipes.decode Encoding.utf8WithoutBom >-> Consumer.stdOutLine

//let pipeline = Producer.fromSeq ["A"; "B"] >-> Pipes.words  >-> Consumer.stdOutLine

//let pipeline = Producer.stdInLine >-> Pipes.words >-> Consumer.stdOutLine
effect
|> Pipes.runEffect
|> Async.RunSynchronously;;