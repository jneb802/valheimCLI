# Reproduce a world and export a small region

At the main menu, with no world running:

```
cli_create_world CliFixture reproducibleSeed
cli_start_local_world CliFixture
```

Creation writes local world metadata through Valheim's save API and re-reads it
to verify the name, seed and identity. It does not generate the world. Existing
names are refused unless `--overwrite` is explicitly supplied. That option
replaces a **local** world, including its old save; it is for disposable fixtures.
A cloud world with that name is always refused. Prefer a new name.

Successful creation replies with `OK: WORLD_CREATED` and the saved world's
name, seed, identity and generation version. Log messages alone are not a
success acknowledgement. Update both the plugin and terminal client: replies
preserve multiline log output, and malformed or incomplete responses now fail
explicitly and disconnect. A response error does not mean creation was undone;
inspect the save before deciding whether to try again. The ValheimCLI does not resend
the command automatically.

In a loaded world:

```
cli_world_dump 8 --window 1000,1000,64
cli_generator_at 1000 1000 1008 1000
```

`cli_generator_at` answers at up to 16 explicit x/z points without writing a
file. It uses the same generator calls and precision as the CSV, one
`GENERATOR x,z height=... blended_height=... biome=... river=... river_width=... base_height=...`
line per point, and ends with `OK: GENERATOR_AT samples=N world=... uid=...`,
so a short independent check can compare selected dump nodes without another
export and confirm the loaded world identity. It is read-only but
cheat-marked: enable devcommands and use strict plugin/world expectations.
Its values are generator facts, not loaded terrain or paint.

The optional output directory goes after the step and before `--window`.
Without a directory, files go below the game's local save directory in
`valheimCLI/world-dumps/<world-name>`. Use different directories to retain runs;
repeating the same export replaces its CSV.

Samples share the full-world lattice `-10000 + index * step`; clipping a window
does not shift it. Coordinates are metres. Step must be 5–1000 metres, or 1–1000
metres with `--window`: the game's heightmap vertices are 1 m apart, and Swamp
and Black Forest ground swings by metres between 8 m nodes. A 1 m window holds
every 8 m and 128 m lattice node inside it, with the same row text as a coarser
dump of that window (`cli_world_dump 1 --window 1000,1000,60` is 121 x 121
samples). Windows are clipped to -10000..10000. Exports exceeding 1,000,000
samples are refused before files are written: use a smaller window or a coarser
step. Export remains synchronous and can pause gameplay; use small windows on a
disposable world.

`world.csv` (or the window-named CSV) has this header:

```
x,z,height,biome,river,river_width,base_height,blended_height
```

Every number is written at full float precision: the shortest invariant-culture
text that parses back to the same float (`R`), so a threshold rule replayed
offline gets the game's verdict. `locations.csv` (`name,x,z,radius`) writes
location centres and radii the same way.

`height` is WorldGenerator.GetHeight: the height of the point's own biome.
`blended_height` is the ground HeightmapBuilder builds for the zone's 64 m
heightmap before terrain modifiers: the four corner biomes' GetBiomeHeight at
the point, mixed with the builder's smoothstep weights (one biome's height when
all four corners agree). The two differ wherever a heightmap's corners are not
all one biome. At a heightmap vertex (every whole metre, so every dump node)
`blended_height` is what an unmodified zone stores there; at a fractional
`cli_generator_at` point it is the same formula at the point, not the game's
interpolation between vertices.
`base_height` is the raw WorldGenerator.GetBaseHeight value, not metres of final
terrain or a client's edited collider height. These are generator data, not
baked terrain edits.
Full exports also write `locations.csv`; windowed exports deliberately do not.
A full export's reply names the location count as `locations=` and that file as
`locations_file=`, so every reply key appears once.
The reply includes the actual sample count, extent and output path. Generated
CSVs, world files and game assemblies do not belong in the repository.
