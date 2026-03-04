这是一个Unity项目,AssetInventory插件是一个资源管理相关插件，其中Assets/AssetInventory/Editor/Scripts/GUI/IndexUI+Packages.cs是与AssetStore Package有关的EditorWindow界面，界面中提供获取用户的购买的资源包列表，能获取资源包的详情得到（大小，下载地址，下载key等信息）,能下载资源包至Unity缓存目录

任务目标：
以Assets/AssetInventory/Editor/Scripts/GUI/IndexUI+Packages.cs为入口，提取获取列表，获取资源详情，下载等接口出来，要求能脱离AssetInventory独立运行，在Assets/UADownloader/Editor/Script/PackageBatchDownloader.cs中实现一个批量资源包下载器，并在下载至Unity缓存目录后，复制文件到导出目录一份，文件名为`资源包名_版本`这样的格式

UI要求
PackageBatchDownloader继承于EditorWindow是一个编辑器窗口，UI元素如下
1. 设置导出目录
2. 概览块（显示资源包总数，已下载数量，未下载数量等）
3. 获取资源列表快照按钮
4. 开始批量下载按钮
5. 暂停批量下载按钮（在开始批量下载后可见）
6. 取消批量下载按钮（在开始批量下载后可见）

操作流程
1. 界面打开用户设置导出目录，任意按钮点击都需要先检查是不是已经设置导出目录才可以操作
2. 查看导出目录packages.json是否存在，如果存在，读取它来得到资源包快照列表。如果不存在，点击获取资源列表快照按钮，获取至用户AssetStore购买的资源列表数据（简略，不完整，不带详情），
3. 获取列表也写入packages.json后，判断导出目录是否存在index.json，如果存在，则说明下载进行中，禁用获取快照按钮，使用缓存列表，如果不存在，批量下载按钮和获取快照都可用
4. 用户点击批量下载时，读取或创建index.json，根据索引队列，获取当前资源包详情，获得详情和下载地址等信息，开始下载，显示进度，单个下载完成后更新index.json以及按上面的要求复制一份至导出目录中
5. 用户可中途取消或暂停，取消则删除index.json，暂停则只是记录停止在当前下载位置，便于下次继续下载