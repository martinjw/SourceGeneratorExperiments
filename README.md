# Source Generator Experiments
Experiments with source generators

.Net source generators first appeared in .net 5, and use Roslyn to generate code. 

Running at compile time, they avoid the runtime impact of reflection (many libraries use Reflection.Emit to front-load the impact). Older ways of doing this include T4 templates. 

Source generators are netstandard2.0 projects and cannot be .net8 or 10. They reference Microsoft.CodeAnalysis nugets. You need a ProjectReference with  OutputItemType="Analyzer". You have to build first (obviously) which generates code that is added to the dll.

The examples here are based on emulating Mediatr and AutoMapper, with cut-down functionality. This repo is not a replacement for those libraries; either use the originals, or the many clones out there. It simply to learn about source generators. In particular, AutoMapper is much richer than this, while Mapster is a variation using source generators.

This solution has 2 generators 
* MediatorLibGenerator.HandlerGenerator. ServiceLib references MediatorLibGenerator as an analyzer (MediatorLibGenerator references ServiceLib so it can read the types). ServiceLib.dll includes the generated code (the WebApi does not reference or include MediatorLibGenerator).
  * To view the generated code, in ServiceLib open Dependencies/Analyzers/MediatorLibGenerator
* RoboMapper has a MappingGenerator. The TestRoboMapper project has 2 references to the project- a conventional project reference, and an Analyzer reference.
  * To view the generated code, in TestRoboMapper open Dependencies/Analyzers/RoboMapper
  * Note the analyzer additional md files for logging diagnostics.

The roboMapper (automapper emulation) is much more complex, and frankly needed a lot of AI help. There are lots of AutoMapper functionalities which are way beyond the scope of this experiment, and frankly that reveals the complexity of the generator can become overwhelming (although as in automapper magic, sometimes a manual mapping class is the better solution).  