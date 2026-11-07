# MediatorMapperGenerator

`MediatorMapperGenerator` is a Roslyn source generator used with the
[`MediatorMapper`](https://www.nuget.org/packages/MediatorMapper) NuGet package.
It generates the mapping and mediator registration code needed by
`MediatorMapper` at compile time.

## Usage

Reference both `MediatorMapper` and `MediatorMapperGenerator` from any project
that contains one or more of the following:

- `MappingProfile` implementations for object mapping
- `RequestHandler` implementations for mediator requests

The generator discovers these types during compilation and generates the
corresponding registrations. The generated code is included in the consuming
project automatically; no runtime reflection-based registration is required.

For details about defining mappings and mediator requests, see the
[`MediatorMapper` repository](https://github.com/martinjw/SourceGeneratorExperiments).
