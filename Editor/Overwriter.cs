/*
unity-overwriter

Copyright (c) 2019 ina-amagami (ina_amagami@gc-career.com)

This software is released under the MIT License.
https://opensource.org/licenses/mit-license.php
*/

using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 同名ファイルをUnity上にドラッグ＆ドロップしても上書きできるようにする
/// </summary>
public class Overwriter : AssetPostprocessor
{
	private class FilePath
	{
		public string Path;
		public string FileName;

		public FilePath(string path)
		{
			Path = path;
			FileName = System.IO.Path.GetFileName(Path);
		}
	}

	private class ExistAsset
	{
		public FilePath Source;
		public FilePath Imported;

		public ExistAsset(FilePath source, FilePath imported)
		{
			Source = source;
			Imported = imported;
		}
	}

	const string SourceExistFormat = "「{0}」と「{1}」のファイル内容が完全一致しているため、置き換えツールの動作を停止します。\n本当にインポートしますか？";
	const string MessageFormat = "「{0}」という名前のアセットが既に存在します。アセットを置き換えますか？";

	static void OnPostprocessAllAssets(
	  string[] importedAssets,
	  string[] deletedAssets,
	  string[] movedAssets,
	  string[] movedFromPath)
	{
		int count = importedAssets.Length;
		if (count == 0 || Event.current == null || Event.current.type != EventType.DragPerform)
		{
			return;
		}

		// ドラッグ＆ドロップ対象に.metaが含まれていた場合はインポート対象外なので除外
		List<string> dragAndDropPaths = new List<string>(DragAndDrop.paths);
		for (int i = 0; i < dragAndDropPaths.Count;)
		{
			if (dragAndDropPaths[i].EndsWith(".meta"))
			{
				dragAndDropPaths.RemoveAt(i);
				continue;
			}
			++i;
		}
		if (count != dragAndDropPaths.Count)
		{
			return;
		}

		// MEMO: プレハブの置き換えはどうやら危険（Re-Serializeが終わらなくなる）なので、一旦除外している
		List<FilePath> sourcePaths = new List<FilePath>(count);
		for (int i = 0; i < count; ++i)
		{
			if (dragAndDropPaths[i].EndsWith(".prefab"))
			{
				continue;
			}
			sourcePaths.Add(new FilePath(dragAndDropPaths[i]));
		}
		List<FilePath> importedPaths = new List<FilePath>(count);
		for (int i = 0; i < count; ++i)
		{
			if (importedAssets[i].EndsWith(".prefab"))
			{
				continue;
			}
			importedPaths.Add(new FilePath(importedAssets[i]));
		}

		// 元ファイルとインポートされたファイルで一致する名前があるかどうかすべて調べる
		int matchCnt = 0;
		for (; matchCnt < count; ++matchCnt)
		{
			string source = sourcePaths[matchCnt].FileName;
			int j = 0;
			for (; j < count; ++j)
			{
				if (source.Contains(importedPaths[j].FileName))
				{
					break;
				}
			}
			if (j == count)
			{
				break;
			}
		}
		// 置き換え処理自体不要
		if (matchCnt == count)
		{
			return;
		}

		// 元ファイルの中に内容が同一なものがあるとうまく動作しないので先に調べる
		bool isExecutable = true;
		bool isDeleteImportedAssets = false;
		for (int i = 0; i < count; ++i)
		{
			for (int j = i + 1; j < count; ++j)
			{
				FilePath path1 = sourcePaths[i];
				FilePath path2 = sourcePaths[j];

				if (FileCompare(path1.Path, path2.Path))
				{
					string message = string.Format(SourceExistFormat, path1.FileName, path2.FileName);
					isDeleteImportedAssets = !EditorUtility.DisplayDialog(
						"確認",
						message,
						"インポート",
						"中止");
					isExecutable = false;
					break;
				}
			}
			if (!isExecutable)
			{
				break;
			}
		}
		if (!isExecutable)
		{
			// インポートされたファイルをすべて消す
			if (isDeleteImportedAssets)
			{
				for (int i = 0; i < importedAssets.Length; ++i)
				{
					AssetDatabase.DeleteAsset(importedAssets[i]);
				}
			}
			return;
		}

		// ファイル名の一致しているものを除外（そのままの名前でインポートできているので重複していない）
		for (int i = 0; i < sourcePaths.Count;)
		{
			bool isRemoved = false;
			FilePath source = sourcePaths[i];
			for (int j = 0; j < importedPaths.Count; ++j)
			{
				FilePath imported = importedPaths[j];
				if (source.FileName != imported.FileName)
				{
					continue;
				}
				// ファイル名が一致していても内容が一致していないケースが存在する
				// 末尾が数字のファイル(hoge0)と同時にその後ろの番号(hoge1)をインポートすると、
				// hoge0がhoge1、hoge1がhoge2として作られる
				if (!FileCompare(source.Path, imported.Path))
				{
					// この場合はhoge1とhoge2を入れ替える
					for (int k = 0; k < importedPaths.Count; ++k)
					{
						if (j == k)
						{
							continue;
						}
						if (FileCompare(source.Path, importedPaths[k].Path))
						{
							string tempPath = imported.Path + "_temp";
							FileUtil.CopyFileOrDirectory(imported.Path, tempPath);
							FileUtil.ReplaceFile(importedPaths[k].Path, imported.Path);
							FileUtil.ReplaceFile(tempPath, importedPaths[k].Path);
							FileUtil.DeleteFileOrDirectory(tempPath);
							AssetDatabase.ImportAsset(imported.Path);
							AssetDatabase.ImportAsset(importedPaths[k].Path);
							break;
						}
					}
				}
				sourcePaths.RemoveAt(i);
				importedPaths.RemoveAt(j);
				isRemoved = true;
				break;
			}
			if (!isRemoved)
			{
				++i;
			}
		}

		// DragAndDrops.pathsはOSの仕様次第で順番がバラバラなので、
		// 実際にインポートされたファイルとソースファイルの内容を見て一致しているものをまとめる
		List<ExistAsset> existAssets = new List<ExistAsset>(sourcePaths.Count);
		for (int i = 0; i < sourcePaths.Count; i++)
		{
			FilePath source = sourcePaths[i];
			for (int j = 0; j < importedPaths.Count; ++j)
			{
				FilePath imported = importedPaths[j];
				if (!FileCompare(source.Path, imported.Path))
				{
					continue;
				}
				existAssets.Add(new ExistAsset(source, imported));
				importedPaths.RemoveAt(j);
				break;
			}
		}
		// ソースファイルのパスでソート
		existAssets.Sort((a, b) => a.Source.Path.CompareTo(b.Source.Path));

		foreach (var exist in existAssets)
		{
			string importedPath = exist.Imported.Path;
			string importedAssetDirectory = Path.GetDirectoryName(importedPath);
			string existingAssetPath = string.Format("{0}/{1}", importedAssetDirectory, exist.Source.FileName);
			string message = string.Format(MessageFormat, exist.Source.FileName);

			int result = EditorUtility.DisplayDialogComplex(
				existingAssetPath.Replace('\\', '/'),
				message,
				"置き換える",
				"中止",
				"両方とも残す");

			if (result == 0)
			{
				FileUtil.ReplaceFile(importedPath, existingAssetPath);
				AssetDatabase.DeleteAsset(importedPath);
				AssetDatabase.ImportAsset(existingAssetPath);
			}
			else if (result == 1)
			{
				AssetDatabase.DeleteAsset(importedPath);
			}
		}
	}

	static bool FileCompare(string file1, string file2)
	{
		if (file1 == file2)
		{
			return true;
		}

		FileStream fs1 = new FileStream(file1, FileMode.Open);
		FileStream fs2 = new FileStream(file2, FileMode.Open);
		int byte1;
		int byte2;
		bool ret = false;

		try
		{
			if (fs1.Length == fs2.Length)
			{
				do
				{
					byte1 = fs1.ReadByte();
					byte2 = fs2.ReadByte();
				}
				while ((byte1 == byte2) && (byte1 != -1));

				if (byte1 == byte2)
				{
					ret = true;
				}
			}
		}
		catch (System.Exception e)
		{
			Debug.LogError(e);
			return false;
		}
		finally
		{
			fs1.Close();
			fs2.Close();
		}

		return ret;
	}
}