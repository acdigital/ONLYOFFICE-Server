/*
 * 
 * (c) Copyright Ascensio System SIA 2010-2014
 * 
 * This program is a free software product.
 * You can redistribute it and/or modify it under the terms of the GNU Affero General Public License
 * (AGPL) version 3 as published by the Free Software Foundation. 
 * In accordance with Section 7(a) of the GNU AGPL its Section 15 shall be amended to the effect 
 * that Ascensio System SIA expressly excludes the warranty of non-infringement of any third-party rights.
 * 
 * This program is distributed WITHOUT ANY WARRANTY; 
 * without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. 
 * For details, see the GNU AGPL at: http://www.gnu.org/licenses/agpl-3.0.html
 * 
 * You can contact Ascensio System SIA at Lubanas st. 125a-25, Riga, Latvia, EU, LV-1021.
 * 
 * The interactive user interfaces in modified source and object code versions of the Program 
 * must display Appropriate Legal Notices, as required under Section 5 of the GNU AGPL version 3.
 * 
 * Pursuant to Section 7(b) of the License you must retain the original Product logo when distributing the program. 
 * Pursuant to Section 7(e) we decline to grant you any rights under trademark law for use of our trademarks.
 * 
 * All the Product's GUI elements, including illustrations and icon sets, as well as technical 
 * writing content are licensed under the terms of the Creative Commons Attribution-ShareAlike 4.0 International. 
 * See the License terms at http://creativecommons.org/licenses/by-sa/4.0/legalcode
 * 
*/

using System;
using System.Collections.Generic;
using ASC.Core;
using ASC.Core.Common.Configuration;
using ASC.Core.Common.Contracts;
using ASC.Notify.Cron;
using ASC.Web.Core.Security;
using ASC.Web.Studio.Utility;
using AjaxPro;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Resources;

namespace ASC.Web.Studio.Core.Backup
{
    [AjaxNamespace("AjaxPro.Backup")]
    internal class BackupAjaxHandler
    {
        [AjaxMethod]
        public BackupProgress StartBackup(BackupStorageType storageType, StorageParams storageParams, bool backupMail)
        {
            DemandPermissions();

            var backupRequest = new StartBackupRequest
                {
                    TenantId = GetCurrentTenantId(),
                    UserId = SecurityContext.CurrentAccount.ID,
                    BackupMail = backupMail,
                    StorageType = storageType
                };

            switch (storageType)
            {
                case BackupStorageType.ThridpartyDocuments: case BackupStorageType.Documents:
                    backupRequest.StorageBasePath = storageParams.FolderId;
                    break;
                case BackupStorageType.CustomCloud:
                    ValidateS3Settings(storageParams.AccessKeyId, storageParams.SecretAccessKey, storageParams.Bucket, storageParams.Region);
                    CoreContext.Configuration.SaveSection(new AmazonS3Settings
                        {
                            AccessKeyId = storageParams.AccessKeyId,
                            SecretAccessKey = storageParams.SecretAccessKey,
                            Bucket = storageParams.Bucket,
                            Region = storageParams.Region
                        });
                    break;
            }
            
            using (var service = new BackupServiceClient())
            {
                return service.StartBackup(backupRequest);
            }
        }

        [AjaxMethod]
        [SecurityPassthrough]
        public BackupProgress GetBackupProgress()
        {
            using (var service = new BackupServiceClient())
            {
                return service.GetBackupProgress(GetCurrentTenantId());
            }
        }

        [AjaxMethod]
        public void DeleteBackup(Guid id)
        {
            DemandPermissions();

            using (var service = new BackupServiceClient())
            {
                service.DeleteBackup(id);
            }
        }

        [AjaxMethod]
        public List<BackupHistoryRecord> GetBackupHistory()
        {
            DemandPermissions();

            using (var service = new BackupServiceClient())
            {
                return service.GetBackupHistory(GetCurrentTenantId());
            }
        }

        [AjaxMethod]
        public BackupProgress StartRestore(string backupId, BackupStorageType storageType, StorageParams storageParams, bool notify)
        {
            DemandPermissions();

            var restoreRequest = new StartRestoreRequest
                {
                    TenantId = GetCurrentTenantId(),
                    NotifyAfterCompletion = notify
                };

            Guid guidBackupId;
            if (Guid.TryParse(backupId, out guidBackupId))
            {
                restoreRequest.BackupId = guidBackupId;
            }
            else
            {
                restoreRequest.StorageType = storageType;
                restoreRequest.FilePathOrId = storageParams.FilePath;
                if (storageType == BackupStorageType.CustomCloud)
                {
                    ValidateS3Settings(storageParams.AccessKeyId, storageParams.SecretAccessKey, storageParams.Bucket, storageParams.Region);
                    CoreContext.Configuration.SaveSection(new AmazonS3Settings
                        {
                            AccessKeyId = storageParams.AccessKeyId,
                            SecretAccessKey = storageParams.SecretAccessKey,
                            Bucket = storageParams.Bucket,
                            Region = storageParams.Region
                        });
                }
            }

            using (var service = new BackupServiceClient())
            {
                return service.StartRestore(restoreRequest);
            }
        }

        [AjaxMethod]
        [SecurityPassthrough]
        public BackupProgress GetRestoreProgress()
        {
            using (var service = new BackupServiceClient())
            {
                return service.GetRestoreProgress(GetCurrentTenantId());
            }
        }

        [AjaxMethod]
        public BackupProgress StartTransfer(string targetRegion, bool notifyUsers, bool transferMail)
        {
            DemandPermissions();

            using (var service = new BackupServiceClient())
            {
                return service.StartTransfer(
                    new StartTransferRequest
                        {
                            TenantId = GetCurrentTenantId(),
                            TargetRegion = targetRegion,
                            BackupMail = transferMail,
                            NotifyUsers = notifyUsers
                        });
            }
        }

        [AjaxMethod]
        [SecurityPassthrough]
        public BackupProgress GetTransferProgress()
        {
            using (var service = new BackupServiceClient())
            {
                return service.GetTransferProgress(GetCurrentTenantId());
            }
        }

        [AjaxMethod]
        public void CreateSchedule(BackupStorageType storageType, StorageParams storageParams, int backupsStored, CronParams cronParams, bool backupMail)
        {
            DemandPermissions();

            ValidateCronSettings(cronParams);
            
            var scheduleRequest = new CreateScheduleRequest
                {
                    TenantId = CoreContext.TenantManager.GetCurrentTenant().TenantId,
                    BackupMail = backupMail,
                    Cron = cronParams.ToString(),
                    NumberOfBackupsStored = backupsStored,
                    StorageType = storageType
                };

            switch (storageType)
            {
                case BackupStorageType.ThridpartyDocuments:
                case BackupStorageType.Documents:
                    scheduleRequest.StorageBasePath = storageParams.FolderId;
                    break;
                case BackupStorageType.CustomCloud:
                    ValidateS3Settings(storageParams.AccessKeyId, storageParams.SecretAccessKey, storageParams.Bucket, storageParams.Region);
                    CoreContext.Configuration.SaveSection(
                        new AmazonS3Settings
                            {
                                AccessKeyId = storageParams.AccessKeyId,
                                SecretAccessKey = storageParams.SecretAccessKey,
                                Bucket = storageParams.Bucket,
                                Region = storageParams.Region
                            });
                    break;
            }

            using (var service = new BackupServiceClient())
            {
                service.CreateSchedule(scheduleRequest);
            }
        }

        [AjaxMethod]
        public Schedule GetSchedule()
        {
            DemandPermissions();

            using (var service = new BackupServiceClient())
            {
                var response = service.GetSchedule(GetCurrentTenantId());
                if (response == null)
                {
                    return null;
                }

                var schedule = new Schedule
                    {
                        StorageType = response.StorageType,
                        StorageParams = new StorageParams(),
                        CronParams = new CronParams(response.Cron),
                        BackupMail = response.BackupMail,
                        BackupsStored = response.NumberOfBackupsStored,
                        LastBackupTime = response.LastBackupTime
                    };

                if (response.StorageType == BackupStorageType.CustomCloud)
                {
                    var amazonSettings = CoreContext.Configuration.GetSection<AmazonS3Settings>();
                    schedule.StorageParams.AccessKeyId = amazonSettings.AccessKeyId;
                    schedule.StorageParams.SecretAccessKey = amazonSettings.SecretAccessKey;
                    schedule.StorageParams.Bucket = amazonSettings.Bucket;
                    schedule.StorageParams.Region = amazonSettings.Region;
                }
                else
                {
                    schedule.StorageParams.FolderId = response.StorageBasePath;
                }

                return schedule;
            }
        }

        [AjaxMethod]
        public void DeleteSchedule()
        {
            DemandPermissions();

            using (var service = new BackupServiceClient())
            {
                service.DeleteSchedule(GetCurrentTenantId());
            }
        }

        private static void DemandPermissions()
        {
            if (!TenantExtra.GetTenantQuota().HasBackup)
                throw new InvalidOperationException(Resource.ErrorNotAllowedOption);

            SecurityContext.DemandPermissions(SecutiryConstants.EditPortalSettings);
        }

        private static void ValidateCronSettings(CronParams cronParams)
        {
            var exp = new CronExpression(cronParams.ToString());
        }

        private static void ValidateS3Settings(string accessKeyId, string secretAccessKey, string bucket, string region)
        {
            if (string.IsNullOrEmpty(accessKeyId))
            {
                throw new ArgumentException("Empty access key id.", "accessKeyId");
            }
            if (string.IsNullOrEmpty(secretAccessKey))
            {
                throw new ArgumentException("Empty secret access key.", "secretAccessKey");
            }
            if (string.IsNullOrEmpty(bucket))
            {
                throw new ArgumentException("Empty s3 bucket.", "bucket");
            }
            if (string.IsNullOrEmpty(region))
            {
                throw new ArgumentException("Empty s3 region.", "region");
            }
            try
            {
                using (var s3 = new AmazonS3Client(accessKeyId, secretAccessKey, new AmazonS3Config { RegionEndpoint = RegionEndpoint.GetBySystemName(region) }))
                {
                    s3.GetBucketLocation(new GetBucketLocationRequest { BucketName = bucket });
                }
            }
            catch (AmazonS3Exception error)
            {
                throw new Exception(error.ErrorCode);
            }
        }

        private static int GetCurrentTenantId()
        {
            return CoreContext.TenantManager.GetCurrentTenant().TenantId;
        }

        public class Schedule
        {
            public BackupStorageType StorageType { get; set; }
            public StorageParams StorageParams { get; set; }
            public CronParams CronParams { get; set; }
            public bool BackupMail { get; set; }
            public int BackupsStored { get; set; }
            public DateTime LastBackupTime { get; set; }
        }

        public class StorageParams
        {
            public string AccessKeyId { get; set; }
            public string SecretAccessKey { get; set; }
            public string Bucket { get; set; }
            public string FolderId { get; set; }
            public string FilePath { get; set; }
            public string Region { get; set; }
        }

        public class CronParams
        {
            public BackupPeriod Period { get; set; }
            public int Hour { get; set; }
            public int Day { get; set; }

            public CronParams()
            {
                
            }

            public CronParams(string cronString)
            {
                var tokens = cronString.Split(' ');
                Hour = Convert.ToInt32(tokens[2]);
                if (tokens[3] != "?")
                {
                    Period = BackupPeriod.EveryMonth;
                    Day = Convert.ToInt32(tokens[3]);
                }
                else if (tokens[5] != "*")
                {
                    Period = BackupPeriod.EveryWeek;
                    Day = Convert.ToInt32(tokens[5]);
                }
                else
                {
                    Period = BackupPeriod.EveryDay;
                }
            }

            public override string ToString()
            {
                switch (Period)
                {
                    case BackupPeriod.EveryDay:
                        return string.Format("0 0 {0} ? * *", Hour);
                    case BackupPeriod.EveryMonth:
                        return string.Format("0 0 {0} {1} * ?", Hour, Day);
                    case BackupPeriod.EveryWeek:
                        return string.Format("0 0 {0} ? * {1}", Hour, Day);
                    default:
                        return base.ToString();
                }
            }
        }

        public enum BackupPeriod
        {
            EveryDay = 0,
            EveryWeek = 1,
            EveryMonth = 2
        }
    }
}