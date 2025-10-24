Building the editor with .NET support:

```bash
scons module_mono_enabled=yes target=editor platform=windows
```

Generating Mono glue with NuGet packages:

```bash
./modules/mono/build_scripts/build_assemblies.py --godot-output-dir ./bin --push-nupkgs-local <my_local_source>
```

Building Windows export templates:

```bash
scons warnings=extra voxel_werror=yes strict_checks=yes platform=windows tests=no target=template_debug arch=x86_64 dev_build=no debug_symbols=no module_mono_enabled=yes mono_glue=yes copy_mono_root=yes mono_static=yes
```
