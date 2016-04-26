﻿using Chloe.DbExpressions;
using Chloe.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Chloe.Descriptors
{
    public class MappingTypeDescriptor
    {
        //TypeMemberValueSetters _typeMemberValueSetters;
        Dictionary<MemberInfo, MappingMemberDescriptor> _mappingMemberDescriptors = new Dictionary<MemberInfo, MappingMemberDescriptor>();
        Dictionary<MemberInfo, NavigationMemberDescriptor> _navigationMemberDescriptors = new Dictionary<MemberInfo, NavigationMemberDescriptor>();

        MappingTypeDescriptor(Type t)
        {
            this.EntityType = t;
            this.Init();
        }

        void Init()
        {
            //_typeMemberValueSetters = TypeMemberValueSetters.GetInstance(this.EntityType);
            this.InitTableInfo();
            this.InitMemberInfo();
        }
        void InitTableInfo()
        {
            Type t = this.EntityType;
            var tableFlags = t.GetCustomAttributes(typeof(TableAttribute), true);

            string tableName;
            if (tableFlags.Length > 0)
            {
                TableAttribute tableFlag = (TableAttribute)tableFlags.First();
                if (tableFlag.Name != null)
                    tableName = tableFlag.Name;
                else
                    tableName = t.Name;
            }
            else
                tableName = t.Name;

            this.Table = new DbTable(tableName);
        }
        void InitMemberInfo()
        {
            Type t = this.EntityType;
            var members = t.GetMembers(BindingFlags.Public | BindingFlags.Instance);

            foreach (var member in members)
            {
                var ignoreFlags = member.GetCustomAttributes(typeof(IgnoreAttribute), true);
                if (ignoreFlags.Length > 0)
                    continue;

                Type memberType = null;
                PropertyInfo prop = null;
                FieldInfo field = null;

                if ((prop = member as PropertyInfo) != null)
                {
                    if (prop.GetSetMethod() == null)
                        continue;//对于没有公共的 setter 直接跳过
                    memberType = prop.PropertyType;
                }
                else if ((field = member as FieldInfo) != null)
                {
                    memberType = field.FieldType;
                }
                else
                    continue;//只支持公共属性和字段

                if (Utils.IsMapType(memberType))
                {
                    MappingMemberDescriptor memberDescriptor = this.ConstructDbFieldDescriptor(member);
                    this._mappingMemberDescriptors.Add(member, memberDescriptor);
                }
                else
                {
                    var associationFlags = member.GetCustomAttributes(typeof(AssociationAttribute), true);
                    if (associationFlags.Length > 0)
                    {
                        AssociationAttribute associationFlag = (AssociationAttribute)associationFlags.First();
                        NavigationMemberDescriptor navigationMemberDescriptor = null;
                        if (member.MemberType == MemberTypes.Property)
                        {
                            navigationMemberDescriptor = new NavigationPropertyDescriptor(prop, this, associationFlag.ThisKey, associationFlag.AssociatingKey);
                        }
                        else if (member.MemberType == MemberTypes.Field)
                        {
                            navigationMemberDescriptor = new NavigationFieldDescriptor(field, this, associationFlag.ThisKey, associationFlag.AssociatingKey);
                        }
                        else
                            continue;

                        this._navigationMemberDescriptors.Add(member, navigationMemberDescriptor);
                    }

                    continue;
                }
            }

            if (this._mappingMemberDescriptors.Values.Where(a => a.IsPrimaryKey).Count() > 1)
            {
                throw new Exception(string.Format("实体类型 {0} 存在多个主键", this.EntityType.FullName));
            }
            //if (this._mappingMemberDescriptors.Values.Where(a => a.IsAutoIncrement).Count() > 1)
            //{
            //    throw new Exception("实体存在多个自增字段");
            //}
        }

        MappingMemberDescriptor ConstructDbFieldDescriptor(MemberInfo member)
        {
            string columnName = null;
            bool isPrimaryKey = false;
            bool isAutoIncrement = false;
            var columnFlags = member.GetCustomAttributes(typeof(ColumnAttribute), true);
            if (columnFlags.Length > 0)
            {
                ColumnAttribute columnFlag = (ColumnAttribute)columnFlags.First();
                if (columnFlag.Name != null)
                    columnName = columnFlag.Name;
                else
                    columnName = member.Name;
                if (columnFlag.IsAutoIncrement)
                {
                    isAutoIncrement = true;
                }
                if (columnFlag.IsPrimaryKey)
                {
                    isPrimaryKey = true;
                }
            }
            else
                columnName = member.Name;


            MappingMemberDescriptor memberDescriptor = null;
            PropertyInfo propertyInfo = member as PropertyInfo;
            if (propertyInfo != null)
            {
                memberDescriptor = new MappingPropertyDescriptor(propertyInfo, this, columnName);
            }
            else
            {
                memberDescriptor = new MappingFieldDescriptor((FieldInfo)member, this, columnName);
            }

            memberDescriptor.IsAutoIncrement = isAutoIncrement;
            memberDescriptor.IsPrimaryKey = isPrimaryKey;

            return memberDescriptor;
        }

        public Type EntityType { get; private set; }
        public DbTable Table { get; private set; }
        public ICollection<MappingMemberDescriptor> MappingMemberDescriptors { get { return this._mappingMemberDescriptors.Values; } }
        public ICollection<NavigationMemberDescriptor> NavigationMemberDescriptors { get { return this._navigationMemberDescriptors.Values; } }

        public MappingMemberDescriptor GetMappingMemberDescriptor(string name)
        {
            MemberInfo memberInfo = this._mappingMemberDescriptors.Keys.Where(a => a.Name == name).FirstOrDefault();
            if (memberInfo == null)
            {
                return null;
            }

            return this._mappingMemberDescriptors[memberInfo];
        }
        public MappingMemberDescriptor GetMappingMemberDescriptor(MemberInfo memberInfo)
        {
            MappingMemberDescriptor memberDescriptor;
            if (!this._mappingMemberDescriptors.TryGetValue(memberInfo, out memberDescriptor))
            {
                return null;
            }

            return memberDescriptor;
        }
        public NavigationMemberDescriptor GetNavigationMemberDescriptor(MemberInfo memberInfo)
        {
            NavigationMemberDescriptor memberDescriptor;
            if (!this._navigationMemberDescriptors.TryGetValue(memberInfo, out memberDescriptor))
            {
                return null;
            }
            return memberDescriptor;
        }

        static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, MappingTypeDescriptor> InstanceCache = new System.Collections.Concurrent.ConcurrentDictionary<Type, MappingTypeDescriptor>();

        public static MappingTypeDescriptor GetEntityDescriptor(Type type)
        {
            MappingTypeDescriptor instance;
            if (!InstanceCache.TryGetValue(type, out instance))
            {
                lock (type)
                {
                    if (!InstanceCache.TryGetValue(type, out instance))
                    {
                        instance = new MappingTypeDescriptor(type);
                        InstanceCache.GetOrAdd(type, instance);
                    }
                }
            }

            return instance;
        }
    }
}
