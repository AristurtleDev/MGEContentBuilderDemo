# Monogame.Extended + MonoGame ContentBuilder Issues

This is a minimal repo project showing the current status and issue when using the new MonoGame ContentBuilder project to import and process Tiled (.tmx) maps using the MonoGame.Extended content pipeline extension.

## Steps To Reproduce

1. Clone this repository

    ```sh
    git clone https://github.com/AristurtleDev/MGEContentBuilderDemo.git
    ```

2. Update submodules. This is needed to get the submodules for MonoGame and MonoGame.Extended

    ```sh
    git submodule update --init --recursive
    ```

3. Open the [`MGEContent.sln`](MGEContent.sln) file in your IDE
4. Run the [`DemoGame/DemoGame.csproj`](DemoGame/DemoGame.csproj) project in Debug mode

## Current Known Issues

### External Reference Building

Tiled tilemaps (.tmx) files can contain various references to external files that it depends on.  For instance, see [`DemoBuilder/Assets/TiledMaps/level05.tmx`](DemoBuilder/Assets/TiledMaps/level05.tmx).  For the `<tileset>` element it shows

```xml
<tileset firstgid="1" name="L05TS1" tilewidth="101" tileheight="171" tilecount="3" columns="3">
    <image source="L05TS1.png" width="303" height="171"/>
 </tileset>
```

This shows that it has an external dependency on the `L05TS1.png` file.  This is an example of a single dependency chain.  If you instead look at [`DemoBuilder/Assets/TiledMaps/level08.tmx`](DemoBuilder/Assets/TiledMaps/level08.tmx), it has the following dependency for the `<tileset>` element:


```xml
<tileset firstgid="1" source="abstractTiles_sheet.tsx"/>
<tileset firstgid="64" source="platformerTiles_sheet.tsx"/>
<tileset firstgid="127" source="voxelTiles_sheet.tsx"/>
```

These are Tiled TileSet (.tsx) files.  These tileset files in turn may also have external dependencies.  For instance, the [`DemoBuilder/Assets/TiledMaps/abstractTiles_sheet.tsx`](DemoBuilder/Assets/TiledMaps/abstractTiles_sheet.tsx) has the following `<tileset>` element:

```xml
<tileset name="abstractTiles_sheet" tilewidth="111" tileheight="128" spacing="1" tilecount="63" columns="9">
    <image source="abstractTiles_sheet.png" width="1024" height="1024"/>
</tileset>
```

Showing that it has a dependency on the `abstractTiles_sheet.png` image. So in this situation, there is a chain of dependencies:

```txt
     level08.tmx           # source asset
         |
         V
abstractTiles_sheet.tsx    # external reference 1
         |
         V
abstractTiles_sheet.png    # external reference 1.A
```

**However, when the ContentBuilder project builds, something is going wrong somewhere and some of the dependencies in the chain are being build as .xnb files and placed back into the `Assets` directory alongside the other raw content files.**

While it appears that everything builds fine (the ContentBuilder project shows no errors), when running the DemoGame, it seems that loading is now broken.  This could very possibly be something on the MonoGame.Extended side, I'm just having trouble finding it if so.

## How External References Are Built

When the ContentBuilder processes a Tiled tilemap (.tmx) file, the following steps occur

### 1. The Import Phase

The [`TiledMapImporter`](external/MonoGame-Extended/source/MonoGame.Extended.Content.Pipeline/Tiled/TiledMapImporter.cs) reads the source `.tmx` file and:

- Deserializes the XML into a  [`TiledMapContent`](external/MonoGame-Extended/source/MonoGame.Extended/Content/Tiled/TiledMapContent.cs) object.
- Identifies all external dependencies (tilesets, images, templates)
- Calls `context.AddDependency()` for each external file
- Returns a [`TiledMapContentItem`](external/MonoGame-Extended/source/MonoGame.Extended.Content.Pipeline/Tiled/TiledMapContentItem.cs) containing the map data

Example from [`TiledMapImporter.cs`](external/MonoGame-Extended/source/MonoGame.Extended.Content.Pipeline/Tiled/TiledMapImporter.cs)

```cs
// For external tileset references
if(!string.IsNullOrWhitespace(tileset.Source))
{
    tileset.Source = getTilesetSource(tileset.Source);

    // Tells pipeline about dependency
    context.AddDependency(tileset.Source);
}
```

### 2. Process Phase

The [`TiledMapProcessor`](external/MonoGame-Extended/source/MonoGame.Extended.Content.Pipeline/Tiled/TiledMapProcessor.cs) takes the imported data and:

- Process the map structure.
- **Builds external references for all dependencies**.
- Stores these references for the writer phase.

This seems to be where the issue is occurring.  For each external dependency, the processor needs to:

**For embedded tilesets with images**:

```cs
// level05.tmx scenario
// image directly in tileset
contentItem.BuildExternalReference<Texture2DContent>(context, tileset.Image);
```

This needs to:

1. Create an [`ExternalReference<TextureContent>`](external/MonoGame/MonoGame.Framework.Content.Pipeline/ExternalReference.cs) pointing to `L05TS1.png`
2. Call `context.BuildAsset()` to queue the texture for building
3. Store the resulting reference in the content item's reference repository

**For External tileset references**:

```cs
// level08.tmx scenario
// external .tsx file
contentItem.BuildExternalReference<TiledMapTilesetContent>(context, tileset.Source);
```

This needs to:

1. Create an [`ExternalReference<TiledMapTilesetContentItem>`](external/MonoGame/MonoGame.Framework.Content.Pipeline/ExternalReference.cs) pointing to `abstractTiles_sheet.tsx`
2. Call `context.BuildAsset()` to queue the tileset for building with [`TiledMapTilesetImporter`](external/MonoGame-Extended/source/MonoGame.Extended.Content.Pipeline/Tiled/TiledMapTilesetImporter.cs) and [`TiledMapTilesetProcessor`](external/MonoGame-Extended/source/MonoGame.Extended.Content.Pipeline/Tiled/TiledMapTilesetProcessor.cs)
3. Store the resulting reference in the content items reference repository

### 3. Nested Processing

When `abstractTiles_sheet.tsx` is processed, by [`TiledMapTilesetProcessor`](external/MonoGame-Extended/source/MonoGame.Extended.Content.Pipeline/Tiled/TiledMapTilesetProcessor.cs), it in turn needs to:

1. Read the tileset XML
2. Find the `<image>` element referencing `abstractTiles_sheet.png`
3. Build an external reference for that texture
4. Store it in the tileset's reference repository

This creates a chain:

```text
level08.tmx (TiledMapProcessor)
    ├─> BuildAsset for abstractTiles_sheet.tsx
    │   └─> TiledMapTilesetProcessor processes it
    │       └─> BuildAsset for abstractTiles_sheet.png
    │           └─> TextureProcessor processes it
    └─> (stores reference to tileset)
```

### 4. Write Phase

The [`TiledMapWriter`](external/MonoGame-Extended/source/MonoGame.Extended.Content.Pipeline/Tiled/TiledMapWriter.cs) serializes the map to XNB format

- Writes the map metadata
- For each tileset, retrieves the external reference from the reference repository
- Writes the external reference to the XNB file using `writer.WriteExternalReference()`
- the reference points to the built tileset or texture XNB file

Example from [`TiledMapWriter.cs`](external/MonoGame-Extended/source/MonoGame.Extended.Content.Pipeline/Tiled/TiledMapWriter.cs):

```cs
private void WriteTileset(ContentWriter writer, TiledMapTilesetContent tileset)
{
    if (!string.IsNullOrWhiteSpace(tileset.Source))
    {
        writer.Write(true);
        // Retrieves the reference that was stored during processing
        writer.WriteExternalReference(
            _contentItem.GetExternalReference<TiledMapTilesetContent>(tileset.Source)
        );
    }
    // ...
}
```

## Complete Dependency Chain Example

Using [`level08.tmx`](DemoBuilder/Assets/TiledMaps/level08.tmx) as an example, here is what the complete build flow with the new API should be(?):

```text
ContentBuilder processes level08.tmx
    │
    ├─> TiledMapImporter.Import("level08.tmx")
    │   └─> Returns TiledMapContentItem with map data
    │
    ├─> TiledMapProcessor.Process(contentItem)
    │   │
    │   ├─> Finds tileset reference: "abstractTiles_sheet.tsx"
    │   │   └─> TiledContentBuildHelpers.BuildTilesetReference()
    │   │       ├─> new TiledMapTilesetImporter()
    │   │       ├─> new TiledMapTilesetProcessor()
    │   │       └─> context.BuildAsset(...)
    │   │           │
    │   │           ├─> Queues abstractTiles_sheet.tsx for processing
    │   │           │   ├─> TiledMapTilesetImporter.Import("abstractTiles_sheet.tsx")
    │   │           │   └─> TiledMapTilesetProcessor.Process(tilesetItem)
    │   │           │       │
    │   │           │       ├─> Finds image reference: "abstractTiles_sheet.png"
    │   │           │       └─> TiledContentBuildHelpers.BuildTextureReference()
    │   │           │           ├─> new TextureImporter()
    │   │           │           ├─> new TextureProcessor()
    │   │           │           └─> context.BuildAsset(...)
    │   │           │               └─> Queues abstractTiles_sheet.png for processing
    │   │           │                   ├─> TextureImporter.Import("abstractTiles_sheet.png")
    │   │           │                   └─> TextureProcessor.Process(texture)
    │   │           │                       └─> Outputs: abstractTiles_sheet.xnb
    │   │           │
    │   │           └─> Outputs: abstractTiles_sheet.xnb (tileset)
    │   │
    │   └─> Returns processed TiledMapContentItem with all external references stored
    │
    └─> TiledMapWriter.Write(contentItem)
        ├─> Writes map metadata
        ├─> Retrieves external reference for abstractTiles_sheet.tsx
        ├─> Writes external reference to XNB
        └─> Outputs: level08.xnb
```
