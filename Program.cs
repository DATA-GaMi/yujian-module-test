using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp4
{
    internal class Program
    {
        // 定义委托和事件，用于在线程完成请求后触发相应逻辑
        public delegate void RequestCompletedHandler(int statusCode, string url);
        public static event RequestCompletedHandler RequestCompleted;

        // 用于记录当前正在运行的线程数量
        static int runningThreadCount = 0;
        // 记录已经访问的URL数量
        static int visitedUrlCount = 0;
        // 记录开始时间
        static DateTime startTime = DateTime.Now;

        // 用于标记是否所有请求任务都已完成（初始为false）
        static bool allTasksCompleted = false;

        static void Main(string[] args)
        {
            var baseUrl = "http://192.168.200.129/";
            string filePath = "ASP.txt";
            List<string> pathList = ReadPathsFromFile(filePath);

            // 遍历路径列表，去除每个路径开头可能存在的斜杠（这里采用了去除斜杠的思路，你可按需换成添加斜杠的逻辑）
            for (int i = 0; i < pathList.Count; i++)
            {
                pathList[i] = pathList[i].TrimStart('/');
            }

            int cpuCount = Environment.ProcessorCount*5;
            Thread[] threads = new Thread[cpuCount];

            // 使用ConcurrentBag来存储结果，它是线程安全的，可在多线程环境下安全添加元素
            ConcurrentBag<(int StatusCode, string Url)> results = new ConcurrentBag<(int StatusCode, string Url)>();

            List<List<string>> splitPathLists = SplitList(pathList, cpuCount);

            for (int i = 0; i < cpuCount; i++)
            {
                int threadIndex = i;
                // 计算每个线程往results集合中添加结果的起始索引（此处逻辑与之前类似，不过现在是用于ConcurrentBag）
                int startIndex = threadIndex * (pathList.Count / cpuCount);
                if (threadIndex < pathList.Count % cpuCount)
                {
                    startIndex += threadIndex;
                }
                else
                {
                    startIndex += pathList.Count % cpuCount;
                }
                threads[i] = new Thread(() =>
                {
                    Interlocked.Increment(ref runningThreadCount); // 线程启动时，原子操作增加正在运行的线程数量
                    List<string> subPathList = splitPathLists[threadIndex];
                    int currentIndex = startIndex;
                    for (int j = 0; j < subPathList.Count; j++)
                    {
                        string fullUrl = baseUrl + subPathList[j];
                        int statusCode = SendHeadRequest(fullUrl);
                        // 将结果以元组形式添加到ConcurrentBag中，这里不管状态码是多少都先添加
                        results.Add((statusCode, fullUrl));
                        // 触发请求完成事件，传递状态码和URL信息，以便外部可以及时处理
                        RequestCompleted?.Invoke(statusCode, fullUrl);
                        Interlocked.Increment(ref visitedUrlCount); // 每访问一个URL，原子操作增加已访问URL数量
                    }
                    Interlocked.Decrement(ref runningThreadCount); // 线程结束时，原子操作减少正在运行的线程数量
                                                                   // 获取当前线程数量（不减1了，只是获取值用于判断）
                    int currentCount = runningThreadCount;
                    // 检查是否所有线程都已结束，如果是，则设置allTasksCompleted为true
                    if (currentCount == 0)
                    {
                        Console.WriteLine("扫描完成");
                        allTasksCompleted = true;
                        
                    }
                });
                threads[i].Start();

            }
            // 订阅请求完成事件，在事件处理程序中判断状态码是否为200并输出
            RequestCompleted += (statusCode, url) =>
            {
                if (statusCode == 200)
                {
                    Console.WriteLine($"访问 {url}，状态码: {statusCode}");
                }
            };

            // 在主线程中定期输出当前正在运行的线程数量以及每秒访问URL数量
            new Thread(() =>
            {
                while (!allTasksCompleted || runningThreadCount > 0)
                {
                    TimeSpan elapsed = DateTime.Now - startTime;
                    int seconds = (int)elapsed.TotalSeconds;
                    if (seconds > 0)
                    {
                        int speed = visitedUrlCount / seconds;
                        Console.WriteLine($"当前正在运行的线程数量: {runningThreadCount}，每秒访问URL数量: {speed}");
                    }
                    Thread.Sleep(1000); // 每隔1秒输出一次信息，可根据需要调整时间间隔
                }
            }).Start();
        }

        static List<string> ReadPathsFromFile(string filePath)
        {
            List<string> paths = new List<string>();
            try
            {
                using (StreamReader reader = new StreamReader(filePath))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        paths.Add(line.Trim());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"读取文件出现异常: {ex.Message}");
            }
            return paths;
        }


        static int SendHeadRequest(string url)
        {
            try
            {
                // 创建HttpWebRequest对象
                HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(url);
                httpWebRequest.Method = "HEAD";

                // 设置一些常用属性
                httpWebRequest.Timeout = 2000;
                httpWebRequest.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/5.0 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36";
                //Console.WriteLine(url);

                // 发送请求并获取响应
                using (HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
                {
                    return (int)httpWebResponse.StatusCode;
                }
            }
            catch (WebException ex)
            {
                if (ex.Response != null)
                {
                    return (int)((HttpWebResponse)ex.Response).StatusCode;
                }
                return -1;
            }
            catch (Exception ex)
            {
                return -1;
            }
        }

        static List<List<string>> SplitList(List<string> sourceList, int parts)
        {
            List<List<string>> result = new List<List<string>>();
            int size = sourceList.Count / parts;
            int remainder = sourceList.Count % parts;

            int startIndex = 0;
            for (int i = 0; i < parts; i++)
            {
                int currentSize = size + (i < remainder ? 1 : 0);
                List<string> subList = sourceList.GetRange(startIndex, currentSize);
                result.Add(subList);
                startIndex += currentSize;
            }

            return result;
        }
    }
}
