using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using NPOI.HSSF.UserModel;
using System.Data;

namespace Intelligent_water_storage_system.Utils
{
    /// <summary>
    /// Excel 读写工具类。
    ///
    /// 【依赖库】NPOI（.NET 版 Apache POI）
    ///   - 支持 .xls（旧格式，HSSFWorkbook）和 .xlsx（新格式，XSSFWorkbook）
    ///   - 无需安装 Microsoft Office，纯 .NET 实现
    ///
    /// 【在本项目中的用途】
    ///   - 读取 DataAddressFile.xls：加载 Modbus 寄存器地址配置
    ///     由 DataPollingService.LoadTags() → ExcelHelper.ExcelToDataTable() 调用
    ///
    /// 【NPOI 核心概念】
    ///   IWorkbook：整个 Excel 工作簿
    ///   ISheet：工作簿中的一个工作表（Sheet）
    ///   IRow：工作表中的一行
    ///   ICell：行中的一个单元格
    /// </summary>
    public class ExcelHelper
    {
        /// <summary>日志回调，由调用方接线</summary>
        public static Action<string, string>? OnLog { get; set; }

        /// <summary>
        /// 将 DataTable 数据导出到 Excel 文件。
        ///
        /// 【执行流程】
        ///   1. 根据文件扩展名创建对应的 Workbook（.xls → HSSFWorkbook，.xlsx → XSSFWorkbook）
        ///   2. 创建 Sheet
        ///   3. 如果 isColumnWritten=true，第一行写入列名（表头）
        ///   4. 逐行逐列写入 DataTable 中的数据
        ///   5. 将 Workbook 写入文件流
        /// </summary>
        /// <param name="data">要导出的数据表</param>
        /// <param name="fileName">目标 Excel 文件路径</param>
        /// <param name="sheetName">工作表名称</param>
        /// <param name="isColumnWritten">是否在第一行写入列名</param>
        /// <returns>写入的数据行数（不含表头），失败返回 -1</returns>
        public static int DataTableToExcel(DataTable data, string fileName, string sheetName, bool isColumnWritten)
        {
            IWorkbook workbook = CreateWorkBook(Path.GetExtension(fileName));
            try
            {
                using (FileStream fs = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite))
                {
                    ISheet sheet = workbook.CreateSheet(sheetName);
                    int count = 0;

                    // 写入表头（列名行）
                    if (isColumnWritten)
                    {
                        IRow row = sheet.CreateRow(0);
                        for (int j = 0; j < data.Columns.Count; ++j)
                        {
                            row.CreateCell(j).SetCellValue(data.Columns[j].ColumnName);
                        }
                        count = 1; // 数据从第 2 行开始写
                    }

                    // 逐行写入数据
                    for (int i = 0; i < data.Rows.Count; ++i)
                    {
                        IRow row = sheet.CreateRow(count);
                        for (int j = 0; j < data.Columns.Count; ++j)
                        {
                            // 所有值都转为字符串写入（NPOI 单元格支持多种类型，这里统一用字符串）
                            row.CreateCell(j).SetCellValue(data.Rows[i][j].ToString());
                        }
                        ++count;
                    }

                    // 将内存中的 Workbook 数据写入文件
                    workbook.Write(fs);
                    return count;
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke("ERROR", $"导出Excel失败: {fileName} - {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// 从 Excel 文件读取数据到 DataTable。
        ///
        /// 【在本项目中的调用链】
        ///   DataPollingService.LoadTags(fileName)
        ///     → ExcelHelper.ExcelToDataTable(fileName, null, true)
        ///       → 返回包含 Tag 配置的 DataTable
        ///         → LoadTags 遍历行解析为 TagAddress 列表
        ///
        /// 【参数说明】
        ///   fileName：Excel 文件完整路径，如 "...\configFiles\DataAddressFile.xls"
        ///   sheetName：工作表名称，传 null 则读取第一个 Sheet
        ///   isFirstRowColumn：第一行是否为列名（true=第一行跳过，从第二行开始读数据）
        /// </summary>
        /// <param name="fileName">Excel 文件路径</param>
        /// <param name="sheetName">Sheet 名称，null 表示读取第一个 Sheet</param>
        /// <param name="isFirstRowColumn">第一行是否为列标题</param>
        /// <returns>包含 Excel 数据的 DataTable</returns>
        public static DataTable ExcelToDataTable(string fileName, string sheetName, bool isFirstRowColumn)
        {
            try
            {
                // 打开文件流（只读模式），读取完毕后自动关闭
                using (FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read))
                {
                    return ReadSheetToDataTable(fs, Path.GetExtension(fileName), sheetName, isFirstRowColumn);
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke("ERROR", $"读取Excel失败: {fileName} - {ex.Message}");
                return new DataTable(); // 返回空表，避免调用方空引用
            }
        }

        /// <summary>
        /// 从 Stream 读取 Excel 数据到 DataTable（流式重载）。
        /// 与文件路径版本共享相同的解析逻辑（ReadSheetToDataTable）。
        ///
        /// 注意：调用方负责管理 stream 的生命周期（关闭/释放）。
        /// </summary>
        public static DataTable ExcelToDataTable(Stream fs, string fileName, string sheetName, bool isFirstRowColumn)
        {
            try
            {
                return ReadSheetToDataTable(fs, Path.GetExtension(fileName), sheetName, isFirstRowColumn);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke("ERROR", $"读取Excel(Stream)失败: {ex.Message}");
                return new DataTable();
            }
        }

        /// <summary>
        /// 从流中读取 Sheet 数据到 DataTable 的核心实现。
        ///
        /// 【解析流程】
        ///   1. 根据扩展名选择 Workbook 实现（.xlsx → XSSFWorkbook，.xls → HSSFWorkbook）
        ///   2. 获取指定的 Sheet（或第一个 Sheet）
        ///   3. 读取第一行：如果是列名模式，将每列的值作为 DataTable 的列名
        ///   4. 从第二行开始逐行读取数据，填入 DataTable 的行
        ///
        /// 【NPOI 单元格读取】
        ///   cell.ToString() 会自动处理各种单元格类型（字符串、数字、布尔等），
        ///   统一返回字符串表示。对于数值型数据（如 Scale、Address），
        ///   由调用方（DataPollingService.LoadTags）进行类型转换。
        /// </summary>
        private static DataTable ReadSheetToDataTable(Stream fs, string extension, string sheetName, bool isFirstRowColumn)
        {
            DataTable data = new DataTable();
            IWorkbook? workbook = null;

            // 根据文件扩展名选择对应的 Workbook 实现
            if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                workbook = new XSSFWorkbook(fs);    // Office 2007+ 格式
            else if (extension.Equals(".xls", StringComparison.OrdinalIgnoreCase))
                workbook = new HSSFWorkbook(fs);    // Office 97-2003 格式

            if (workbook == null)
                return data; // 不支持的格式，返回空表

            // 获取 Sheet：优先按名称查找，找不到则取第一个 Sheet
            ISheet sheet = sheetName != null
                ? workbook.GetSheet(sheetName) ?? workbook.GetSheetAt(0)
                : workbook.GetSheetAt(0);

            if (sheet == null)
                return data; // Sheet 不存在，返回空表

            // 读取第一行（表头或数据，取决于 isFirstRowColumn）
            IRow firstRow = sheet.GetRow(0);
            if (firstRow == null)
                return data; // 空表，返回空表

            int cellCount = firstRow.LastCellNum; // 第一行的列数
            int startRow; // 数据起始行号

            if (isFirstRowColumn)
            {
                // 第一行为列名：读取每列的名称，创建 DataTable 的列
                for (int i = firstRow.FirstCellNum; i < cellCount; ++i)
                {
                    ICell cell = firstRow.GetCell(i);
                    if (cell != null)
                    {
                        string cellValue = cell.StringCellValue;
                        if (cellValue != null)
                            data.Columns.Add(new DataColumn(cellValue));
                    }
                }
                startRow = sheet.FirstRowNum + 1; // 数据从第 2 行开始
            }
            else
            {
                startRow = sheet.FirstRowNum; // 数据从第 1 行开始
            }

            // 逐行读取数据
            int rowCount = sheet.LastRowNum;
            for (int i = startRow; i <= rowCount; ++i)
            {
                IRow row = sheet.GetRow(i);
                if (row == null) continue; // 跳过空行

                DataRow dataRow = data.NewRow();
                for (int j = row.FirstCellNum; j < cellCount; ++j)
                {
                    if (row.GetCell(j) != null)
                        dataRow[j] = row.GetCell(j).ToString();
                }
                data.Rows.Add(dataRow);
            }

            return data;
        }

        /// <summary>
        /// 将泛型 List 数据导出到 Excel。
        /// 使用反射获取对象属性值，按 colNames 中指定的属性名和列名写入。
        ///
        /// 【参数 colNames】
        ///   Key = 属性名（用于反射取值），Value = Excel 中的列名（表头显示文本）
        ///   例如：new Dictionary&lt;string, string&gt; { {"Name", "姓名"}, {"Age", "年龄"} }
        /// </summary>
        public static int ListToExcel<T>(List<T> list, string fileName, string sheetName, Dictionary<string, string> colNames)
        {
            IWorkbook workbook = CreateWorkBook(Path.GetExtension(fileName));
            try
            {
                using (FileStream fs = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite))
                {
                    ISheet sheet = workbook.CreateSheet(sheetName);
                    List<string> keys = new List<string>(colNames.Keys);
                    int count = 0;

                    // 写入表头行
                    if (colNames.Count > 0)
                    {
                        IRow headerRow = sheet.CreateRow(0);
                        for (int j = 0; j < keys.Count; ++j)
                        {
                            headerRow.CreateCell(j).SetCellValue(colNames[keys[j]]);
                        }
                        count = 1;
                    }

                    // 使用反射获取每个对象的属性值并写入
                    Type type = typeof(T);
                    for (int i = 0; i < list.Count; ++i)
                    {
                        IRow row = sheet.CreateRow(count);
                        for (int j = 0; j < keys.Count; ++j)
                        {
                            // 反射获取属性值
                            var p = type.GetProperty(keys[j]);
                            object val = p?.GetValue(list[i]) ?? "";
                            row.CreateCell(j).SetCellValue(val.ToString());
                        }
                        ++count;
                    }

                    workbook.Write(fs);
                    return count;
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke("ERROR", $"导出List到Excel失败: {fileName} - {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// 根据文件扩展名创建对应的 IWorkbook 实例。
        /// .xlsx → XSSFWorkbook（Office 2007+ 格式，基于 XML）
        /// .xls → HSSFWorkbook（Office 97-2003 格式，二进制）
        /// </summary>
        public static IWorkbook CreateWorkBook(string extension)
        {
            if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                return new XSSFWorkbook();
            if (extension.Equals(".xls", StringComparison.OrdinalIgnoreCase))
                return new HSSFWorkbook();
            throw new ArgumentException($"不支持的 Excel 格式: {extension}", nameof(extension));
        }

        /// <summary>
        /// 在指定的 Workbook 中创建新的 Sheet。
        /// </summary>
        public static ISheet CreateSheet(IWorkbook workbook, string sheetName)
        {
            ArgumentNullException.ThrowIfNull(workbook);
            return workbook.CreateSheet(sheetName);
        }
    }
}
