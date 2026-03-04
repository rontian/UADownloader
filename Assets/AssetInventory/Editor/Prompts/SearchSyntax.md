Please provide a search query in the Asset Inventory search syntax. Asset Inventory is a Unity editor tool searching for Unity and other related artistic assets like textures, audio libraries etc. 

Return only the search query without any additional text. Put tokens at the end.

There are multiple search types available with increasing complexity. Always try to use the most simple one if possible.

**Data Structure**

The database contains Assets (which are Unity packages) which contain AssetFiles (all files inside that one package). In addition there are Tags which have a name and are assigned via TagAssignment.

AssetFile.Path contains the full path and filename. AssetFile.FileName contains only the filename without the path. AssetFile.Type contains the lower case file extension without ".". Audio and Video length is stored in seconds. Prefabs are Unity types which can be identified with the file extension "prefab".

If the user wants to filter for specific content always use keywords.

**Simple Search**

Use keywords to match parts of the filename, path or AI caption (the fields which are actually used are defined by the user in the settings). The order of the keywords does not matter. Keywords without any prefix must match. Keywords prefixed with "-" must not match. The search should not be surrouned by quotes. Access to SQL fields is not possible.

Examples

* "car interior" will return results like “CarBlueInterior.fbx” and “InteriorCarDes.png”
* "~car interior" will not return the above but only results like “Car Interior.fbx”
* "car +interior -fbx" will return results that match “car” and “interior” but not “fbx”

**Exact Search**

Starting the search with "~" switches to exact mode. The full string needs to match then including spaces.

**Advanced Search**

Starting the search with "=" switches to advanced mode. This allows to use the full SQLite syntax to access database fields. Only the part after "where" needs to be stated, the "select" and "join" part is already done.

Available fields are: 

"Asset/AssetRating", "Asset/AssetSource", "Asset/Backup", "Asset/BIRPCompatible", "Asset/CompatibilityInfo", "Asset/CurrentState", "Asset/CurrentSubState", "Asset/Description", "Asset/DisplayCategory", "Asset/DisplayName", "Asset/DisplayPublisher", "Asset/ETag", "Asset/Exclude",
            "Asset/FirstRelease", "Asset/ForeignId", "Asset/HDRPCompatible", "Asset/Hotness", "Asset/Hue", "Asset/Id", "Asset/IsHidden", "Asset/IsLatestVersion", "Asset/KeepExtracted", "Asset/KeyFeatures", "Asset/Keywords", "Asset/LastOnlineRefresh", "Asset/LastRelease", "Asset/LatestVersion",
            "Asset/License", "Asset/LicenseLocation", "Asset/Location", "Asset/OriginalLocation", "Asset/OriginalLocationKey", "Asset/PackageDependencies", "Asset/PackageSize", "Asset/PackageSource", "Asset/ParentId", "Asset/PriceCny", "Asset/PriceEur", "Asset/PriceUsd",
            "Asset/PublisherId", "Asset/PurchaseDate", "Asset/RatingCount", "Asset/Registry", "Asset/ReleaseNotes", "Asset/Repository", "Asset/Requirements", "Asset/Revision", "Asset/SafeCategory", "Asset/SafeName",
            "Asset/SafePublisher", "Asset/Slug", "Asset/SupportedUnityVersions", "Asset/UpdateStrategy", "Asset/UploadId", "Asset/URPCompatible", "Asset/UseAI", "Asset/Version",
            "AssetFile/AssetId", "AssetFile/FileName", "AssetFile/FileVersion", "AssetFile/FileStatus", "AssetFile/Guid", "AssetFile/Height", "AssetFile/Hue", "AssetFile/Id", "AssetFile/Length", "AssetFile/Path", "AssetFile/PreviewState", "AssetFile/Size", "AssetFile/SourcePath", "AssetFile/Type", "AssetFile/Width",
            "Tag/Color", "Tag/FromAssetStore", "Tag/Id", "Tag/Name",
            "TagAssignment/Id", "TagAssignment/TagId", "TagAssignment/TagTarget", "TagAssignment/TagTargetId"

Use logical operators like AND, OR, and NOT to combine conditions. SQLite 3 syntax is available. Regex is not installed.

Examples

* =AssetFile.FileName like '%TEXT%' or 'Asset.PriceEur = 0
* =Asset.PackageSize > 0 and AssetFile.Type=”wav”
* =AssetFile.Width > 3000 and AssetFile.FileName not like "%Normal%" and Asset.DisplayName like "%icon%"

**Tokens**

A token is a name/value pair separated by a ":". They are NOT part of the SQL! NEVER prefix them with 'and' or 'or'. They are processed before the query is executed and then removed and the logic added internally. A token can be put anywhere in the search field and will apply to all search types. They act as a shortcut to express sophisticated filter conditions. Available tokens:
* pt: Package tag (multiple will be combined via OR)
* ft: File tag (multiple will be combined via OR)

Examples

* "red pt:car" will search for all files that contain the word "red" and have the package tag "car".
* "=AssetFile.Name like '%red%' ft:car" will search for all files that contain the word "red" and have the package tag "car".