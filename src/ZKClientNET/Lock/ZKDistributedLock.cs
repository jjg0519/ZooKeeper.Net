﻿using log4net;
using System;
using System.Collections.Generic;
using System.Threading;
using ZKClientNET.Client;
using ZKClientNET.Exceptions;
using ZKClientNET.Listener;
using ZooKeeperNet;

namespace ZKClientNET.Lock
{
    /// <summary>
    /// 分布式锁
    /// 非线程安全，每个线程请单独创建实例
    /// </summary>
    public class ZKDistributedLock : IZKLock
    {
        private static readonly ILog LOG = LogManager.GetLogger(typeof(ZKDistributedLock));
        private IZKChildListener countListener;
        private ZKClient client;
        private string lockPath;
        private string currentSeq;
        private Semaphore semaphore;
        public string lockNodeData;

        private ZKDistributedLock(ZKClient client, string lockPach)
        {
            this.client = client;
            this.lockPath = lockPach;
            IZKChildListener childListener = new ZKChildListener().ChildChange(
            (parentPath, currentChilds) =>
            {
                if (Check(currentSeq, currentChilds))
                {
                    semaphore.Release();
                }
            });
            this.countListener = childListener;
        }

        /// <summary>
        /// 判断路径是否可以获得锁，如果checkPath 对应的序列是所有子节点中最小的，则可以获得锁。
        /// </summary>
        /// <param name="checkPath"></param>
        /// <param name="children"></param>
        /// <returns></returns>
        private bool Check(string checkPath, List<string> children)
        {
            if (children == null || !children.Contains(checkPath))
            {
                return false;
            }
            //判断checkPath 是否是children中的最小值，如果是返回true，不是返回false
            long chePathSeq = long.Parse(checkPath);
            bool isLock = true;
            foreach (string path in children)
            {
                long pathSeq = long.Parse(path);
                if (chePathSeq > pathSeq)
                {
                    isLock = false;
                    break;
                }
            }
            return isLock;
        }

        /// <summary>
        /// 创建分布式锁实例的工厂方法
        /// </summary>
        /// <param name="client"></param>
        /// <param name="lockPach"></param>
        /// <returns></returns>
        public static ZKDistributedLock NewInstance(ZKClient client, string lockPach)
        {
            if (!client.Exists(lockPach))
            {
                throw new ZKNoNodeException("The lockPath is not exists!,please create the node.[path:" + lockPach + "]");
            }
            ZKDistributedLock zkDistributedLock = new ZKDistributedLock(client, lockPach);
            //对lockPath进行子节点数量的监听
            client.SubscribeChildChanges(lockPach, zkDistributedLock.countListener);
            return zkDistributedLock;
        }

        public bool Lock()
        {
            return Lock(0) ;
        }

        /// <summary>
        /// 获得锁
        /// </summary>
        /// <param name="timeout">
        /// 如果超时间大于0，则会在超时后直接返回false。
        /// 如果超时时间小于等于0，则会等待直到获取锁为止。
        /// </param>
        /// <returns>成功获得锁返回true，否则返回false</returns>
        public bool Lock(int timeout)
        {
            semaphore = new Semaphore(1, 1);
            string newPath = client.Create(lockPath + "/1", lockNodeData, CreateMode.EphemeralSequential);
            string[] paths = newPath.Split(new string[] { "/" }, StringSplitOptions.RemoveEmptyEntries);
            currentSeq = paths[paths.Length - 1];
            bool getLock = false;
            try
            {
                if (timeout > 0)
                {
                    getLock = semaphore.WaitOne(timeout);
                }
                else
                {
                    semaphore.WaitOne();
                    getLock = true;
                }
            }
            catch (ThreadInterruptedException e)
            {
                throw new ZKInterruptedException(e);
            }
            if (getLock)
            {
                LOG.Debug("get lock successful.");
            }
            else
            {
                LOG.Debug("failed to get lock.");
            }
            return getLock;
        }

        /// <summary>
        /// 释放
        /// </summary>
        /// <returns>
        /// 如果释放锁成功返回true，否则返回false
        ///释放锁失败只有一种情况，就是线程正好获得锁，在释放之前，
        /// 与服务器断开连接，这时候服务器会自动删除EPHEMERAL_SEQUENTIAL节点。
        ///在会话过期之后再删除节点就会删除失败，因为路径已经不存在了。
        /// </returns>
        public bool UnLock()
        {
            client.UnSubscribeChildChanges(lockPath, countListener);
            return client.Delete(lockPath + "/" + currentSeq);
        }

        /// <summary>
        /// 获得所有参与者的节点名称
        /// </summary>
        /// <returns></returns>
        public List<string> GetParticipantNodes()
        {
            List<string> children = client.GetChildren(lockPath);
            children.Sort(CompareRow);
            return children;
        }

        private int CompareRow(string lhs, string rhs)
        {
            return lhs.CompareTo(rhs);
        }
   
      
    }
}
