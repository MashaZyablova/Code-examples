using NameSpace_ChangeListWindow;
using NameSpace_DataSource;
using NameSpace_NamedObject;
using NameSpace_NewOpenSaveFunctions;
using NameSpace_NumberConversions;
using NameSpace_ObservableDictionary;
using NameSpace_Project;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UcColorSelectorConfig;

namespace UniversalConfig
{

    /// <summary>
    /// Логика взаимодействия для UniversalConfigWindow.xaml
    /// </summary>
    public partial class UniversalConfigWindow : Window
    {
        public List<Task> Tasks { get; set; }
        public dynamic ObjectConfig { get; set; }

        //последний выбранный в дереве узел
        TreeViewItem LastSelectedTreeViewItem;
        //словарь, ключ - пара (текущий тип, родительский тип), значение - контекстное меню
        Dictionary<PropertyInfo, ContextMenu> ContextMenu = new Dictionary<PropertyInfo, ContextMenu>();
        //словарь, ключ - тип, значение - список его подтипов
        Dictionary<Type, HashSet<Type>> ChildTypes = new Dictionary<Type, HashSet<Type>>();
        //Список элементов управления, которые нужно блокировать в случае невалидных данных
        List<UIElement> UIElementsToBlock;
        //Индикатор валидности заполненных данных
        public bool ValidData = true;

        public UniversalConfigWindow(dynamic objectConfig, List<Task> tasks)
        {
            InitializeComponent();

            UIElementsToBlock = new List<UIElement>()
            {
                Tree, TableImmediateDataSource, ComboBoxImmediateObjectsDictionary,
                /*ButtonImmediateObjectsClusters,*/ TableImmediateObjectsDictionary, ButtonImmediateObjectsFillFromFile,
                ComboBoxImmediateList, TableImmediateListDataSource, ComboBoxImmediateListDictionary,
                /*ButtonImmediateListClusters,*/ TableImmediateListDictionary, ButtonImmediateListFillFromFile, ComboBoxChildObjects,
                TableChildObjectsDataSource, ComboBoxChildObjectsDictionary, /*ButtonChildObjectsClusters,*/
                TableChildObjectsDictionary, ButtonChildObjectsFillFromFile, ComboBoxChildObjectsDataSourceList, TableChildObjectsListDataSource,
                ComboBoxChildObjectsListDictionary, /*ButtonChildObjectsListClusters,*/ TableChildObjectsListDictionary, ButtonChildObjectsListFillFromFile
            };
            ChildTypes.Add(typeof(MultiDataSourceWithMapping), new HashSet<Type> { typeof(Mapping) });
            ChildTypes.Add(typeof(Mapping), new HashSet<Type>() { typeof(Mapping) });
            ObjectConfig = objectConfig;

            Tasks = tasks;

            Type classType = objectConfig.GetType();
            RecursivePass(classType, this.GetType().GetProperty("ObjectConfig"));
            //Проверка на актуальность текущей конфигурации приложения
            RecursiveCheckConfig(this, this.GetType().GetProperty("ObjectConfig"),
                classType, tasks);
            //Построение дерева по объекту конфигурации
            RecursiveTreeBuilding(this, this.GetType().GetProperty("ObjectConfig"),
                classType, Tree);

            ((TreeViewItem)Tree.Items[0]).IsSelected = true;
            LastSelectedTreeViewItem = (TreeViewItem)Tree.Items[0];
            UpdateTables();

            TableImmediateListDataSource.ContextMenu.Tag = TableImmediateListDataSource;
            TableChildObjectsListDataSource.ContextMenu.Tag = TableChildObjectsListDataSource;
            this.Closing += UniversalConfigWindow_Closing;

            ((NameCheckRuleInTable)TableImmediateListDataSource.RowValidationRules[0]).DataGrid = TableImmediateListDataSource;
            ((NameCheckRuleInTable)TableChildObjectsListDataSource.RowValidationRules[0]).DataGrid = TableChildObjectsListDataSource;
            ((NameCheckRuleInTable)TableListDataSourceOfMultiDataSourceWithMapping.RowValidationRules[0]).DataGrid = TableListDataSourceOfMultiDataSourceWithMapping;
            ((NameCheckRuleInTable)TableImmediateListDataSource.RowValidationRules[0]).BlockUIElementsMethod = BlockUIElements;
            ((NameCheckRuleInTable)TableChildObjectsListDataSource.RowValidationRules[0]).BlockUIElementsMethod = BlockUIElements;
            ((NameCheckRuleInTable)TableListDataSourceOfMultiDataSourceWithMapping.RowValidationRules[0]).BlockUIElementsMethod = BlockUIElements;

            ((CheckIndexValidationRule)TableImmediateObjectsDictionary.RowValidationRules[0]).DataGrid = TableImmediateObjectsDictionary;
            ((CheckIndexValidationRule)TableImmediateListDictionary.RowValidationRules[0]).DataGrid = TableImmediateListDictionary;
            ((CheckIndexValidationRule)TableChildObjectsDictionary.RowValidationRules[0]).DataGrid = TableChildObjectsDictionary;
            ((CheckIndexValidationRule)TableChildObjectsListDictionary.RowValidationRules[0]).DataGrid = TableChildObjectsListDictionary;
            ((CheckIndexValidationRule)TableDictionaryWithMapping.RowValidationRules[0]).DataGrid = TableDictionaryWithMapping;
            ((CheckIndexValidationRule)TableImmediateObjectsDictionary.RowValidationRules[0]).BlockUIElementsMethod = BlockUIElements;
            ((CheckIndexValidationRule)TableImmediateListDictionary.RowValidationRules[0]).BlockUIElementsMethod = BlockUIElements;
            ((CheckIndexValidationRule)TableChildObjectsDictionary.RowValidationRules[0]).BlockUIElementsMethod = BlockUIElements;
            ((CheckIndexValidationRule)TableChildObjectsListDictionary.RowValidationRules[0]).BlockUIElementsMethod = BlockUIElements;
            ((CheckIndexValidationRule)TableDictionaryWithMapping.RowValidationRules[0]).BlockUIElementsMethod = BlockUIElements;

            TableImmediateObjectsDictionary.Tag = ComboBoxImmediateObjectsDictionary;
            TableImmediateListDictionary.Tag = ComboBoxImmediateListDictionary;
            TableChildObjectsDictionary.Tag = ComboBoxChildObjectsDictionary;
            TableChildObjectsListDictionary.Tag = ComboBoxChildObjectsListDictionary;
        }

        private void UniversalConfigWindow_Closing(object sender, CancelEventArgs e)
        {
            if (!ValidData)
            {
                e.Cancel = true;
                return;
            }

            bool cancelResult = true;
            UniversalConfigWindow universalConfigWindow = (UniversalConfigWindow)sender;
            NewOpenSaveFunctions.FormClose(universalConfigWindow.ObjectConfig, ref cancelResult, out System.Windows.Forms.DialogResult result);

            if (!cancelResult)
                e.Cancel = true;
            DialogResult = result == System.Windows.Forms.DialogResult.Yes;
        }

        #region Проверка на актуальность текущей конфигкрации приложения
        /// <summary>
        /// Проверка на актуальность текущей конфигурации приложения
        /// </summary>
        /// <typeparam name="P"></typeparam>
        /// <param name="parentObject">Объект, свойство которое анализируется</param>
        /// <param name="analizingProperty">Информация об анализируемом свойстве объекта</param>
        /// <param name="typeOfAnalizingProperty">Тип содержащийся в анализируемом свойстве</param>
        /// <param name="tasks">Список актуальных заданий с ядра</param>
        private void RecursiveCheckConfig<P>(P parentObject, PropertyInfo analizingProperty,
            Type typeOfAnalizingProperty, List<Task> tasks)
        {
            //Если анализируемое свойство является DataSourceVisual или его наследником
            if (analizingProperty.PropertyType == typeof(DataSourceVisual) ||
                analizingProperty.PropertyType.IsSubclassOf(typeof(DataSourceVisual)))
            {
                CheckDataSourceConfig((DataSourceVisual)analizingProperty.GetValue(parentObject), tasks);
                return;
            }


            Type parentObjectType = parentObject.GetType();
            PropertyInfo[] properties = typeOfAnalizingProperty.GetProperties();
            //получает список объектов, тип которых соответсвует свойству prop родителя parent
            //например, список частей у машины
            dynamic collection = analizingProperty.GetValue(parentObject);
            Type typeOfCollection = collection.GetType();

            //Проверка на то, является ли анализируемое свой
            if (typeOfCollection.GetInterfaces().
                Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)).Count() != 0)
            {
                foreach (dynamic element in collection)
                {
                    //Если анализиуемая коллекция типа Dictionary, то element имеет
                    //тип KeyValuePair
                    if (element.GetType().IsGenericType &&
                        (typeOfAnalizingProperty == typeof(DataSourceVisual) || typeOfAnalizingProperty.IsSubclassOf(typeof(DataSourceVisual))))
                        //Анализируемое свойство является Dictionary<..., :DataSourceVisual>  
                        CheckDataSourceConfig(element.Value, tasks);
                    else if (typeOfAnalizingProperty == typeof(DataSourceVisual) || typeOfAnalizingProperty.IsSubclassOf(typeof(DataSourceVisual)))
                        //Анализируемое свойство является List<:DataSourceVisual>  
                        CheckDataSourceConfig(element, tasks);
                    //рекурсивный вызов для подтипов currentType
                    else if (ChildTypes.ContainsKey(typeOfAnalizingProperty))
                    {
                        foreach (PropertyInfo property in properties)
                        {
                            dynamic propertyValue;
                            //Если анализиуемая коллекция типа Dictionary, то element имеет
                            //тип KeyValuePair
                            if (element.GetType().IsGenericType)
                                propertyValue = property.GetValue(element.Value);
                            else
                                propertyValue = property.GetValue(element);
                            if (propertyValue == null)
                                continue;
                            Type type = propertyValue.GetType();

                            if (type.IsGenericType)
                            {
                                Type[] subTypes = type.GetGenericArguments();
                                // Словарь
                                if (IsDictionaryOfNamedObjects(typeOfCollection))
                                {
                                    if (ChildTypes[typeOfAnalizingProperty].Contains(subTypes[0]))
                                        RecursiveCheckConfig(element.Value, property, subTypes[0], tasks);
                                    else if (subTypes.Count() > 1 && ChildTypes[typeOfAnalizingProperty].Contains(subTypes[1]))
                                        RecursiveCheckConfig(element.Value, property, subTypes[1], tasks);
                                }
                                // Список
                                if (IsListOrCollectionOfNamedObjects(typeOfCollection))
                                {
                                    if (element.GetType().IsGenericType)
                                    {
                                        if (ChildTypes[typeOfAnalizingProperty].Contains(subTypes[0]))
                                            RecursiveCheckConfig(element[0], property, subTypes[0], tasks);
                                        else if (subTypes.Count() > 1 && ChildTypes[typeOfAnalizingProperty].Contains(subTypes[1]))
                                            RecursiveCheckConfig(element[0], property, subTypes[1], tasks);
                                    }
                                    else
                                    {
                                        if (ChildTypes[typeOfAnalizingProperty].Contains(subTypes[0]))
                                            RecursiveCheckConfig(element, property, subTypes[0], tasks);
                                        else if (subTypes.Count() > 1 && ChildTypes[typeOfAnalizingProperty].Contains(subTypes[1]))
                                            RecursiveCheckConfig(element, property, subTypes[1], tasks);
                                    }
                                }
                            }
                            else if (ChildTypes[typeOfAnalizingProperty].Contains(type))
                            {
                                if (IsDictionaryOfNamedObjects(typeOfCollection)) // Словарь
                                    RecursiveCheckConfig(element.Value, property, type, tasks);
                                else if (IsListOrCollectionOfNamedObjects(typeOfCollection)) // Список
                                    RecursiveCheckConfig(element, property, type, tasks);
                            }
                        }
                    }
                }
            }
            else // NamedObject или MultiDataSourceWithMapping
            {
                //рекурсивный вызов для подтипов currentType
                if (ChildTypes.ContainsKey(typeOfAnalizingProperty))
                {
                    foreach (PropertyInfo property in properties)
                    {
                        dynamic propertyValue = property.GetValue(collection);
                        Type type = propertyValue.GetType();

                        if (type.IsGenericType)
                        {
                            Type[] subTypes = type.GetGenericArguments();
                            if (ChildTypes[typeOfAnalizingProperty].Contains(subTypes[0]))
                                RecursiveCheckConfig(collection, property, subTypes[0], tasks);

                            if (subTypes.Count() > 1 && ChildTypes[typeOfAnalizingProperty].Contains(subTypes[1]))
                                RecursiveCheckConfig(collection, property, subTypes[1], tasks);
                        }
                        else if (ChildTypes[typeOfAnalizingProperty].Contains(type))
                            RecursiveCheckConfig(collection, property, type, tasks);
                    }
                }
            }
        }

        /// <summary>
        /// Проверка актуальности текущей конфигурации DataSourceVisual или его наследника
        /// </summary>
        /// <param name="dataSource"></param>
        /// <param name="tasks"></param>
        private void CheckDataSourceConfig(DataSourceVisual dataSource, List<Task> tasks)
        {
            int projectVersion = dataSource.ProjectVersion;
            int taskVersion = dataSource.TaskVersion;

            //Проверка на то, что существует задание по тому же проекту
            List<Task> sameProjectTasks = tasks.FindAll(x => x.ProjectVersion == projectVersion).ToList();
            if (sameProjectTasks.Count == 0)
            {
                dataSource.TaskName = null;
                dataSource.DataBlockName = null;
                dataSource.VariableName = null;
                return;
            }
            //Проверка на то, что задание сохранилось
            Task task = sameProjectTasks.Find(x => x.TaskVersion == taskVersion);
            if (task != null)
            {
                dataSource.TaskName = task.Name;
                return;
            }
            //Поиск максимальной версии задания по тому же проекту
            int maxTaskVersion = sameProjectTasks.Select(y => y.TaskVersion).Max();
            task = sameProjectTasks.Find(x => x.TaskVersion == maxTaskVersion);
            dataSource.TaskVersion = maxTaskVersion;
            dataSource.TaskName = task.Name;
            //Проверка на существование блока данных в новом задании
            DataBlock dataBlock = task.AvailableDataBlocks.Find(x => x.Name == dataSource.DataBlockName);
            if (dataBlock == null)
            {
                dataSource.DataBlockName = null;
                dataSource.VariableName = null;
                return;
            }
            //Проверка на существование переменной в новом задании
            Variable variable = dataBlock.Variables.Find(x => (x.Name == dataSource.VariableName) &&
            (x.Kind == dataSource.VariableKind));
            if (variable == null)
                dataSource.VariableName = null;
        }
        #endregion

        #region Перестроение дерева
        /// <summary>
        /// Построение контекстного меню
        /// </summary>
        /// <param name="classType">Тип рассматриваемого класса</param>
        /// <param name="analizingProperty">Рассматриваемое поле рассматриваемого класса</param>
        public void RecursivePass(Type classType, PropertyInfo analizingProperty)
        {
            //Список типов содержащихся в определении рассматриваемого класса
            List<Type> typesContainedInClassType = new List<Type>();
            //Список свойств рассматриваемого класса
            PropertyInfo[] classTypeProperties = classType.GetProperties();

            //Создание контекстного меню
            ContextMenu menuStrip = new ContextMenu();

            if (!(ContextMenu.ContainsKey(analizingProperty)))
                ContextMenu.Add(analizingProperty, menuStrip);

            if (!classType.IsSubclassOf(typeof(DataSourceVisual)))
            {
                foreach (PropertyInfo property in classTypeProperties)  // пробегается по свойствам текущего класса
                {
                    Type type = property.PropertyType;
                    var attributes = property.GetCustomAttributes(typeof(DescriptionAttribute), false);

                    if (type.IsGenericType && classType != typeof(MultiDataSourceWithMapping)) //Интересуют только генерируемые типы: List<...>, Dictionary<..., ...>
                    {
                        string name;
                        if (attributes.Count() > 0)
                        {
                            DescriptionAttribute attribute = (DescriptionAttribute)attributes[0];
                            name = attribute.Description;
                        }
                        else
                            name = property.Name.ToString();
                        Type containedType = null;
                        if (IsDictionaryOfNamedObjects(type))
                            containedType = type.GetGenericArguments()[1];
                        else if (IsListOrCollectionOfNamedObjects(type))
                            containedType = type.GetGenericArguments()[0];
                        else
                            continue;
                        typesContainedInClassType.Add(containedType);

                        MenuItem menuItem = new MenuItem()
                        { Header = "Редактировать " + name };
                        menuStrip.Items.Add(menuItem);
                        menuItem.Click += EditObjectsMenuItem_Click;
                        menuItem.Tag = property;
                        RecursivePass(containedType, property);
                    }
                    else if (type.IsSubclassOf(typeof(NamedObject)) || type == typeof(MultiDataSourceWithMapping))
                    {
                        typesContainedInClassType.Add(type);
                        RecursivePass(type, property);
                    }
                    else if (classType == typeof(MultiDataSourceWithMapping) && type == typeof(ObservableDictionary<int, Mapping>))
                    {
                        Type mappingType = type.GetGenericArguments()[1];
                        PropertyInfo[] mappingTypeProperties = mappingType.GetProperties();
                        bool contextMenuIsNeeded = true;
                        foreach (PropertyInfo propertyInfo in mappingTypeProperties)
                        {
                            if (propertyInfo.PropertyType == typeof(ObservableDictionary<int, string>))
                                contextMenuIsNeeded = false;
                        }
                        if (contextMenuIsNeeded)
                        {
                            MenuItem menuItem1 = new MenuItem()
                            { Header = "Разъединить все словари уровня" };
                            menuStrip.Items.Add(menuItem1);
                            menuItem1.Click += DisconnectDictionaryMenuItem_Click;
                            menuItem1.Tag = property;
                        }
                    }
                }
                if (typesContainedInClassType.Count != 0)
                    if (ChildTypes.ContainsKey(classType))
                        ChildTypes[classType].UnionWith(typesContainedInClassType);
                    else
                        ChildTypes.Add(classType, new HashSet<Type>(typesContainedInClassType));
            }


            //Непосредственное поле, которое является наследником класса NamedObject, не может быть удалено или переименовано пользователем
            if (!analizingProperty.PropertyType.IsSubclassOf(typeof(NamedObject)) &&
                analizingProperty != this.GetType().GetProperty("ObjectConfig") && classType != typeof(Mapping))
            {
                MenuItem RenameMenuItem = new MenuItem() { Header = "Переименовать" };
                menuStrip.Items.Add(RenameMenuItem);
                RenameMenuItem.Tag = analizingProperty;
                RenameMenuItem.Click += RenameMenuItem_Click;
                MenuItem DeleteMenuItem = new MenuItem() { Header = "Удалить" };
                menuStrip.Items.Add(DeleteMenuItem);
                DeleteMenuItem.Tag = analizingProperty;
                DeleteMenuItem.Click += DeleteMenuItem_Click;
            }
        }

        /// <summary>
        /// Строит дерево
        /// </summary>
        /// <typeparam name="P"></typeparam>
        /// <param name="parentObject">Родительский объект</param>
        /// <param name="analizingProperty">Исследуемое свойство родительского объекта</param>
        /// <param name="typeOfAnalizingProperty">Тип содержащийся в определении исследуемого свойства</param>
        /// <param name="node"></param>
        private void RecursiveTreeBuilding<P>(P parentObject, PropertyInfo analizingProperty,
            Type typeOfAnalizingProperty, dynamic node)
        {
            //Не создаем узлы для непосредственных полей - наследников DataSourceVisual
            if ((!typeOfAnalizingProperty.IsSubclassOf(typeof(NamedObject))
                && typeOfAnalizingProperty != typeof(MultiDataSourceWithMapping)) ||
                analizingProperty.PropertyType == typeof(DataSourceVisual) ||
                analizingProperty.PropertyType.IsSubclassOf(typeof(DataSourceVisual)))
                return;


            Type parentObjectType = parentObject.GetType();
            PropertyInfo[] properties = typeOfAnalizingProperty.GetProperties();
            //получает список объектов, тип которых соответсвует свойству prop родителя parent
            //например, список частей у машины
            dynamic collection = analizingProperty.GetValue(parentObject);
            Type typeOfCollection = collection.GetType();

            if (typeOfCollection.GetInterfaces().
                Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)).Count() != 0)
            {
                foreach (dynamic element in collection)
                {
                    TreeViewItem currentNode;
                    if (IsDictionaryOfNamedObjects(typeOfCollection)) // Словарь
                    {
                        if (typeOfCollection.GenericTypeArguments[0] == typeof(string))
                        {
                            currentNode = new TreeViewItem() { Header = element.Key };
                            currentNode.IsExpanded = true;
                        }
                        else if (typeOfCollection.GenericTypeArguments[0] == typeof(int))
                        {
                            currentNode = new TreeViewItem() { Header = element.Value.Name };
                            currentNode.IsExpanded = true;
                        }
                        else
                            return;
                    }
                    else if (IsListOrCollectionOfNamedObjects(typeOfCollection))  // Список
                    {
                        currentNode = new TreeViewItem() { Header = element.Name };
                        currentNode.IsExpanded = true;
                    }
                    else
                        return;
                    node.Items.Add(currentNode);
                    currentNode.Expanded += TreeViewItem_Expanded;
                    currentNode.Tag = element;
                    if (element.GetType() == typeof(ObservableKeyValuePair<int, Mapping>))
                    {
                        ContextMenu menuStrip = new ContextMenu();
                        MenuItem menuItem = new MenuItem()
                        { Header = "Сделать словарь общим для уровня" };
                        menuStrip.Items.Add(menuItem);
                        menuItem.Click += CommonDictionaryMenuItem_Click;
                        menuItem.Tag = analizingProperty;


                        dynamic type = element.Value.TextView.GetType();
                        if (type != typeof(ObservableDictionary<int, string>))
                        {
                            MenuItem menuItem1 = new MenuItem()
                            { Header = "Разъединить все словари уровня" };
                            menuStrip.Items.Add(menuItem1);
                            menuItem1.Click += DisconnectDictionaryMenuItem_Click;
                            menuItem1.Tag = analizingProperty;
                        }
                        currentNode.ContextMenu = menuStrip;
                    }
                    else if (ContextMenu.ContainsKey(analizingProperty))
                    {
                        currentNode.ContextMenu = ContextMenu[analizingProperty];
                    }


                    //рекурсивный вызов для подтипов currentType
                    if (ChildTypes.ContainsKey(typeOfAnalizingProperty))
                    {
                        foreach (PropertyInfo property in properties)
                        {
                            dynamic propertyValue;
                            //Если анализиуемая коллекция типа Dictionary, то element имеет
                            //тип KeyValuePair
                            if (element.GetType().IsGenericType)
                                propertyValue = property.GetValue(element.Value);
                            else
                                propertyValue = property.GetValue(element);
                            if (propertyValue == null)
                                continue;
                            Type type = propertyValue.GetType();

                            if (type.IsGenericType)
                            {
                                Type[] subTypes = type.GetGenericArguments();
                                if (IsDictionaryOfNamedObjects(typeOfCollection)) // Словарь
                                {
                                    if (ChildTypes[typeOfAnalizingProperty].Contains(subTypes[0]))
                                        RecursiveTreeBuilding(element.Value, property, subTypes[0], currentNode);
                                    else if (subTypes.Count() > 1 && ChildTypes[typeOfAnalizingProperty].Contains(subTypes[1]))
                                        RecursiveTreeBuilding(element.Value, property, subTypes[1], currentNode);
                                }
                                if (IsListOrCollectionOfNamedObjects(typeOfCollection))// Список
                                {
                                    if (element.GetType().IsGenericType)
                                    {
                                        if (ChildTypes[typeOfAnalizingProperty].Contains(subTypes[0]))
                                            RecursiveTreeBuilding(element[0], property, subTypes[0], currentNode);
                                        else if (subTypes.Count() > 1 && ChildTypes[typeOfAnalizingProperty].Contains(subTypes[1]))
                                            RecursiveTreeBuilding(element[0], property, subTypes[1], currentNode);
                                    }
                                    else
                                    {
                                        if (ChildTypes[typeOfAnalizingProperty].Contains(subTypes[0]))
                                            RecursiveTreeBuilding(element, property, subTypes[0], currentNode);
                                        else if (subTypes.Count() > 1 && ChildTypes[typeOfAnalizingProperty].Contains(subTypes[1]))
                                            RecursiveTreeBuilding(element, property, subTypes[1], currentNode);
                                    }
                                }
                            }
                            else if (ChildTypes[typeOfAnalizingProperty].Contains(type))
                            {
                                if (IsDictionaryOfNamedObjects(typeOfCollection)) // Словарь
                                    RecursiveTreeBuilding(element.Value, property, type, currentNode);
                                else if (IsListOrCollectionOfNamedObjects(typeOfCollection)) // Список
                                    RecursiveTreeBuilding(element, property, type, currentNode);
                            }
                        }
                    }
                }
            }
            else // NamedObject или MultiDataSourceWithMapping
            {
                TreeViewItem currentNode;
                if (typeOfAnalizingProperty.IsSubclassOf(typeof(NamedObject)))
                {
                    currentNode = new TreeViewItem() { Header = collection.Name };
                    currentNode.IsExpanded = true;
                    node.Items.Add(currentNode);
                    currentNode.Tag = collection;
                    currentNode.ContextMenu = ContextMenu[analizingProperty];
                    currentNode.Expanded += TreeViewItem_Expanded;
                    if (collection.GetType() == typeof(MultiDataSourceWithMapping))
                        TableDictionaryWithMapping.Tag = currentNode;
                }
                else
                    currentNode = node;

                //рекурсивный вызов для подтипов currentType
                if (ChildTypes.ContainsKey(typeOfAnalizingProperty))
                {
                    foreach (PropertyInfo property in properties)
                    {
                        dynamic propertyValue = property.GetValue(collection); // список DataSourceVisual
                        Type type = propertyValue.GetType();
                        Type typeOfElements = null;
                        if (type.IsGenericType)
                            typeOfElements = type.GetGenericArguments()[0];
                        //Для MultiDataSourceWithMapping список DataSourceVisual не строится в дереве
                        if (typeOfElements == typeof(DataSourceVisual) && typeOfCollection == typeof(MultiDataSourceWithMapping))
                            continue;
                        if (type.IsGenericType)
                        {
                            Type[] subTypes = type.GetGenericArguments();
                            if (ChildTypes[typeOfAnalizingProperty].Contains(subTypes[0]))
                                RecursiveTreeBuilding(collection, property, subTypes[0], currentNode);

                            if (subTypes.Count() > 1 && ChildTypes[typeOfAnalizingProperty].Contains(subTypes[1]))
                                RecursiveTreeBuilding(collection, property, subTypes[1], currentNode);
                        }
                        else if (ChildTypes[typeOfAnalizingProperty].Contains(type))
                            RecursiveTreeBuilding(collection, property, type, currentNode);
                    }
                }
            }
        }

        private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
        {
            TreeViewItem selectedItem = (TreeViewItem)sender;
            if (selectedItem != null && selectedItem.IsExpanded == true)
                TreeViewItemRebuild(selectedItem);
            e.Handled = true;
        }

        //Обновление дерева в случае изменения имен объектов в коллекции ObservableCollection<:DataSourceVisual> в 
        //таблице с непосредственным списком выделенного в дереве объекта 
        public void UpdateNamesInListDataSourceTree(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "Name")
                return;
            TreeViewItem treeViewItem = TreeViewItemToRebuildTree();
            if (!treeViewItem.IsExpanded)
                return;
            TreeViewItemRebuild(treeViewItem);
            if (treeViewItem != LastSelectedTreeViewItem)
                foreach (TreeViewItem item in treeViewItem.Items)
                {
                    if (item.Tag == LastSelectedTreeViewItem.Tag)
                    {
                        item.IsSelected = true;
                        LastSelectedTreeViewItem = item;
                        break;
                    }
                }
        }
        //Обновление дерева в случае изменения коллекции ObservableCollection<:DataSourceVisual> в 
        //таблице с непосредственным списком выделенного в дереве объекта 
        public void UpdateListDataSourceTree(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems == null)
                return;
            TreeViewItem treeViewItem = TreeViewItemToRebuildTree();
            if (!treeViewItem.IsExpanded)
                return;
            TreeViewItemRebuild(treeViewItem);
            if (treeViewItem != LastSelectedTreeViewItem)
                foreach (TreeViewItem item in treeViewItem.Items)
                {
                    if (item.Tag == LastSelectedTreeViewItem.Tag)
                    {
                        item.IsSelected = true;
                        LastSelectedTreeViewItem = item;
                        break;
                    }
                }
        }


        public TreeViewItem TreeViewItemToRebuildTree()
        {
            Type type = LastSelectedTreeViewItem.Tag.GetType();
            if (IsDataSourceHeir(type) || IsImmediateDataSource(type) || type.Name[0] == '_')
            {
                return (TreeViewItem)LastSelectedTreeViewItem.Parent;
            }
            else
            {
                return LastSelectedTreeViewItem;
            }
        }

        // Полное перестроение узла дерева
        public void TreeViewItemRebuild(dynamic item)
        {
            object tag;
            Type itemTagType = item.Tag.GetType();
            if (itemTagType == typeof(MultiDataSourceWithMapping) || itemTagType == typeof(ObservableKeyValuePair<int, Mapping>))
                return;
            if (itemTagType.IsGenericType) // Если генерируемый тип
                if (itemTagType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)) // Если пара ключ - значение
                    tag = item.Tag.Value;
                else
                    return;
            else // Если список
                tag = item.Tag;

            //Родительский объект
            dynamic parentObject = tag;
            item.Items.Clear();
            Type typeOfParent = parentObject.GetType();
            PropertyInfo[] parentProperties = typeOfParent.GetProperties();
            //Перестроение родительского узла
            foreach (PropertyInfo property in parentProperties)
            {
                Type type = property.PropertyType;
                if (!type.IsGenericType && !type.IsSubclassOf(typeof(NamedObject)))
                    continue;

                if (type.IsGenericType)
                {
                    Type[] subTypes = type.GetGenericArguments();
                    if (IsDictionaryOfNamedObjects(type))
                        RecursiveTreeBuilding(parentObject, property, subTypes[1], item);
                    else if (IsListOrCollectionOfNamedObjects(type))
                        RecursiveTreeBuilding(parentObject, property, subTypes[0], item);
                }
                else
                    RecursiveTreeBuilding(parentObject, property, type, item);
            }
            item.IsExpanded = true;
        }

        #endregion

        #region Контекстное меню

        /// <summary>
        /// кнопка Редактировать в контекстном меню
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void EditObjectsMenuItem_Click(object sender, EventArgs e)
        {
            object tag;
            dynamic selectedNode = (TreeViewItem)Tree.SelectedItem;

            Type selectedNodeTagType = selectedNode.Tag.GetType();
            if (selectedNodeTagType.IsGenericType) // Если генерируемый тип
                if (selectedNodeTagType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)) // Если пара ключ - значение
                    tag = selectedNode.Tag.Value;
                else
                    return;
            else // Если список
                tag = selectedNode.Tag;

            //Родительский объект
            dynamic parentObject = tag;
            //Рассматриваемое свойство родительского объекта
            PropertyInfo analizingProperty = (PropertyInfo)((MenuItem)sender).Tag;
            //Тип рассматриваемого свойство родительского объекта
            Type typeOfAnalizingProperty = analizingProperty.PropertyType;
            //Редактируемая коллекция объектов
            dynamic collection = analizingProperty.GetValue(parentObject);
            //Тип элементов в коллекции
            Type typeOfCollectionElements;

            if (collection.GetType().GetGenericTypeDefinition() == typeof(ObservableCollection<>))
                (collection as INotifyCollectionChanged).CollectionChanged -= UpdateListDataSourceTree;


            List<NamedObject> tempList = new List<NamedObject>();
            foreach (var element in collection)
            {
                if (typeOfAnalizingProperty.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                    tempList.Add((NamedObject)element.Value);
                else
                    tempList.Add((NamedObject)element);
            }

            //Редактируемое свойство должно быть генерируемым типом
            //(List<NamedObject>, Dictionary<int, NamedObject>, Dictionary<string, NamedObject>)
            if (typeOfAnalizingProperty.IsGenericType)
            {
                string name;
                var attributes = analizingProperty.GetCustomAttributes(typeof(DescriptionAttribute), false);
                if (attributes.Count() > 0)
                {
                    DescriptionAttribute attribute = (DescriptionAttribute)attributes[0];
                    name = attribute.Description;
                }
                else
                    name = analizingProperty.Name.ToString();
                //Заполнение словаря
                if (typeOfAnalizingProperty.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                {
                    typeOfCollectionElements = typeOfAnalizingProperty.GetGenericArguments()[1];
                    var editListForm = new ChangeListWindow(tempList, typeOfCollectionElements, null, -1, "Редактирование коллекции " + name);
                    editListForm.ShowDialog();
                    collection.Clear();
                    foreach (dynamic element in editListForm.NamedObjects)
                    {
                        //Заполнение словаря типа <int, NamedObject>
                        if (typeOfAnalizingProperty.GetGenericArguments()[0] == typeof(int))
                            collection.Add(editListForm.NamedObjects.IndexOf(element) + 1, element);
                        //Заполнение словаря типа <string, NamedObject>
                        else if (typeOfAnalizingProperty.GetGenericArguments()[0] == typeof(string))
                            collection.Add(element.Name, element);
                    }
                }
                //Заполнение списка
                else if (typeOfAnalizingProperty.GetGenericTypeDefinition() == typeof(List<>) ||
                        typeOfAnalizingProperty.GetGenericTypeDefinition() == typeof(ObservableCollection<>))
                {
                    typeOfCollectionElements = typeOfAnalizingProperty.GetGenericArguments()[0];
                    var editListForm = new ChangeListWindow(tempList, typeOfCollectionElements, null, -1, "Редактирование коллекции " + name);
                    editListForm.ShowDialog();
                    collection.Clear();
                    foreach (dynamic element in editListForm.NamedObjects)
                        collection.Add(element);
                }
            }
            else
                return;

            selectedNode.Items.Clear();

            Type typeOfParent = parentObject.GetType();
            PropertyInfo[] parentProperties = typeOfParent.GetProperties();
            //Перестроение родительского узла
            foreach (PropertyInfo property in parentProperties)
            {
                Type type = property.PropertyType;
                if (!type.IsGenericType && !type.IsSubclassOf(typeof(NamedObject)))
                    continue;

                if (type.IsGenericType)
                {
                    Type[] subTypes = type.GetGenericArguments();
                    if (type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                        RecursiveTreeBuilding(parentObject, property, subTypes[1], selectedNode);
                    else
                        RecursiveTreeBuilding(parentObject, property, subTypes[0], selectedNode);
                }
                else
                    RecursiveTreeBuilding(parentObject, property, type, selectedNode);
            }
            selectedNode.IsExpanded = true;
            UpdateTables();
        }

        /// <summary>
        /// кнопка Удалить в контекстном меню
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DeleteMenuItem_Click(object sender, EventArgs e)
        {
            object tag;
            //Родительский узел
            TreeViewItem parentNode = (TreeViewItem)((TreeViewItem)Tree.SelectedItem).Parent;
            //Узел для удаления
            TreeViewItem nodeToDelete = (TreeViewItem)Tree.SelectedItem;
            //Тэг привязанный к родительскому узлу
            dynamic parentNodeTag = parentNode.Tag;
            //Тип тэга привязанного к родительскому узлу
            Type parentNodeTagType = parentNodeTag.GetType();
            // Если генерируемый тип
            if (parentNodeTagType.IsGenericType)
            {
                // Если привязана пара ключ - родительский объект
                if (parentNodeTagType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                    tag = parentNodeTag.Value;
                else
                    return;
            }
            else // Если привязан сам родительскомй объект
                tag = parentNode.Tag;
            //Узел для удаления
            dynamic nodeToDeleteTag = nodeToDelete.Tag;
            //Родительский объект
            dynamic parentObject = tag;
            //Свойство родительского объекта, соответствующего редактируемой коллекции
            PropertyInfo property = (PropertyInfo)((MenuItem)sender).Tag;
            //Редактируемая коллекция объектов
            dynamic collectionToDeleteFrom = property.GetValue(parentObject);

            Type typeOfElements = null;
            if (property.PropertyType.IsGenericType) // Если тип редактируемой коллекции генерируемый
            {
                if (property.PropertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>)) // Если словарь
                {
                    collectionToDeleteFrom.Remove(nodeToDeleteTag.Key);
                    typeOfElements = property.PropertyType.GetGenericArguments()[1];
                }
                else if (property.PropertyType.GetGenericTypeDefinition() == typeof(List<>) ||
                        property.PropertyType.GetGenericTypeDefinition() == typeof(ObservableCollection<>)) // Если список
                {
                    for (int i = 0; i < collectionToDeleteFrom.Count; i++)
                    {
                        if (object.ReferenceEquals(collectionToDeleteFrom[i], nodeToDeleteTag))
                        {
                            collectionToDeleteFrom.RemoveAt(i);
                        }
                    }
                    typeOfElements = property.PropertyType.GetGenericArguments()[0];
                }
            }
            else // Если список
                return;

            parentNode.Items.Clear();
            Type typeOfParent = parentObject.GetType();
            PropertyInfo[] parentProperties = typeOfParent.GetProperties();
            foreach (PropertyInfo parentProperty in parentProperties)
            {
                Type type = parentProperty.PropertyType;
                if (!type.IsGenericType && !type.IsSubclassOf(typeof(NamedObject)))
                    continue;

                if (type.IsGenericType)
                {
                    Type[] subTypes = type.GetGenericArguments();
                    if (type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                        RecursiveTreeBuilding(parentObject, parentProperty, subTypes[1], parentNode);
                    else
                        RecursiveTreeBuilding(parentObject, parentProperty, subTypes[0], parentNode);
                }
                else
                    RecursiveTreeBuilding(parentObject, parentProperty, type, parentNode);
            }
            parentNode.IsExpanded = true;
            UpdateTables();
        }


        /// <summary>
        /// кнопка Переименовать в контекстном меню
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RenameMenuItem_Click(object sender, EventArgs e)
        {
            object tag;
            //Родительский узел
            TreeViewItem parentNode = (TreeViewItem)((TreeViewItem)Tree.SelectedItem).Parent;
            //Узел для переимнования
            TreeViewItem selectedNode = (TreeViewItem)Tree.SelectedItem;
            //Тип тэга привязанного к родительскому узлу
            Type parentNodeTagType = parentNode.Tag.GetType();
            //Тэг привязанный к родительскому узлу
            dynamic parentNodeTag = parentNode.Tag;
            // Если генерируемый тип
            if (parentNodeTagType.IsGenericType)
            {
                // Если привязана пара ключ - родительский объект
                if (parentNodeTagType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                    tag = parentNodeTag.Value;
                else
                    return;
            }
            else // Если привязан сам родительскомй объект
                tag = parentNode.Tag;

            //Родительский объект
            dynamic parentObject = tag;
            //получение выбранного объекта
            Type selectedNodeTagType = selectedNode.Tag.GetType();
            dynamic selectedNodeTag = selectedNode.Tag;
            // Если генерируемый тип
            if (selectedNodeTagType.IsGenericType)
            {
                if (selectedNodeTagType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                    tag = selectedNodeTag.Value;
                else
                    return;
            }
            else
                tag = selectedNode.Tag;
            dynamic selectedObject = tag;

            //Свойство родительского объекта, соответствующего редактируемой коллекции
            PropertyInfo property = (PropertyInfo)((MenuItem)sender).Tag;
            //Редактируемая коллекция объектов
            dynamic collection = property.GetValue(parentObject);

            Type typeOfElements;

            if (collection.GetType().GetGenericTypeDefinition() == typeof(ObservableCollection<>))
                (collection as INotifyCollectionChanged).CollectionChanged -= UpdateListDataSourceTree;

            //Тип редактируемой коллекции
            Type typeOfProperty = property.PropertyType;
            List<NamedObject> tempList = new List<NamedObject>();
            if (typeOfProperty.IsGenericType)  // Если тип редактируемой коллекции генерируемый
            {
                string name;
                var attributes = property.GetCustomAttributes(typeof(DescriptionAttribute), false);
                if (attributes.Count() > 0)
                {
                    DescriptionAttribute attribute = (DescriptionAttribute)attributes[0];
                    name = attribute.Description;
                }
                else
                    name = property.Name.ToString();

                if (typeOfProperty.GetGenericTypeDefinition() == typeof(Dictionary<,>)) // Если тип редактируемой коллекции словарь
                {
                    typeOfElements = typeOfProperty.GetGenericArguments()[1];
                    foreach (var element in collection)
                    {
                        tempList.Add((NamedObject)element.Value);
                    }
                    var RenameForm = new ChangeListWindow(tempList, typeOfElements, null, -1, "Переименование коллекции " + name, selectedObject);
                    RenameForm.ShowDialog();

                    collection.Clear();
                    foreach (dynamic element in RenameForm.NamedObjects)
                    {
                        if (typeOfProperty.GetGenericArguments()[0] == typeof(string))
                        {
                            collection.Add(element.Name, element);
                        }
                        else if (typeOfProperty.GetGenericArguments()[0] == typeof(int))
                        {
                            collection.Add(RenameForm.NamedObjects.IndexOf(element), element);
                        }
                    }
                }
                else if (typeOfProperty.GetGenericTypeDefinition() == typeof(List<>) ||
                         typeOfProperty.GetGenericTypeDefinition() == typeof(ObservableCollection<>))  // Если тип редактируемой коллекции список
                {
                    typeOfElements = typeOfProperty.GetGenericArguments()[0];
                    foreach (var element in collection)
                    {
                        tempList.Add((NamedObject)element);
                    }
                    var RenameForm = new ChangeListWindow(tempList, typeOfElements, null, -1, "Переименование коллекции " + name, selectedObject);
                    RenameForm.ShowDialog();

                    collection.Clear();
                    foreach (dynamic element in RenameForm.NamedObjects)
                    {
                        collection.Add(element);
                    }
                }
                else return;
            }
            else return;

            parentNode.Items.Clear();
            Type typeOfParent = parentObject.GetType();
            PropertyInfo[] parentProperties = typeOfParent.GetProperties();
            foreach (PropertyInfo parentProperty in parentProperties)
            {
                Type type = parentProperty.PropertyType;
                if (!type.IsGenericType && !type.IsSubclassOf(typeof(NamedObject)))
                    continue;

                if (type.IsGenericType)
                {
                    Type[] subTypes = type.GetGenericArguments();
                    if (type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                        RecursiveTreeBuilding(parentObject, parentProperty, subTypes[1], parentNode);
                    else
                        RecursiveTreeBuilding(parentObject, parentProperty, subTypes[0], parentNode);
                }
                else
                    RecursiveTreeBuilding(parentObject, parentProperty, type, parentNode);
            }
            parentNode.IsExpanded = true;
            UpdateTables();
        }

        /// <summary>
        /// Переименование из файла
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RenameFromFileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();

            Nullable<bool> result = openFileDialog.ShowDialog();
            if (result != true)
            {
                return;
            }
            if (openFileDialog.FileName == null)
            {
                return;
            }
            System.IO.StreamReader fileStream = new System.IO.StreamReader(openFileDialog.FileName);
            string line;
            string variableName;
            string dataSourceName;
            Dictionary<string, string> renameDictionary = new Dictionary<string, string>();
            while ((line = fileStream.ReadLine()) != null)
            {
                String[] lineSplit = line.Split('|');
                if (lineSplit.Count() != 2)
                {
                    MessageBox.Show("Неверный формат файла.");
                    return;
                }
                variableName = lineSplit[0].Trim();
                dataSourceName = lineSplit[1].Trim();
                renameDictionary.Add(variableName, dataSourceName);
            }
            fileStream.Close();
            DataGrid datagrid = (DataGrid)((ContextMenu)((MenuItem)sender).Parent).Tag;
            dynamic list = datagrid.ItemsSource;
            foreach (dynamic dataSource in list)
            {
                if (renameDictionary.ContainsKey(dataSource.Name))
                    dataSource.Name = renameDictionary[dataSource.Name];
            }
            object tag;
            dynamic selectedNode = (TreeViewItem)Tree.SelectedItem;

            Type selectedNodeTagType = selectedNode.Tag.GetType();
            if (selectedNodeTagType.IsGenericType) // Если генерируемый тип
                if (selectedNodeTagType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)) // Если пара ключ - значение
                    tag = selectedNode.Tag.Value;
                else
                    return;
            else // Если список
                tag = selectedNode.Tag;

            //Родительский объект
            dynamic parentObject = tag;
            selectedNode.Items.Clear();
            Type typeOfParent = parentObject.GetType();
            PropertyInfo[] parentProperties = typeOfParent.GetProperties();
            //Перестроение родительского узла
            foreach (PropertyInfo property in parentProperties)
            {
                Type type = property.PropertyType;
                if (!type.IsGenericType && !type.IsSubclassOf(typeof(NamedObject)))
                    continue;

                if (type.IsGenericType)
                {
                    Type[] subTypes = type.GetGenericArguments();
                    if (type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                        RecursiveTreeBuilding(parentObject, property, subTypes[1], selectedNode);
                    else
                        RecursiveTreeBuilding(parentObject, property, subTypes[0], selectedNode);
                }
                else
                    RecursiveTreeBuilding(parentObject, property, type, selectedNode);
            }
            selectedNode.IsExpanded = true;
            UpdateTables();
        }
        /// <summary>
        /// Заполнение списка переменными из блока данных
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AddAllDataSourcesFromDataBlockMenuItem_Click(object sender, RoutedEventArgs e)
        {
            AddAllDataSourcesFromDataBlockWindow window = new AddAllDataSourcesFromDataBlockWindow(Tasks);
            bool result = (bool)window.ShowDialog();
            DataGrid datagrid = (DataGrid)((ContextMenu)((MenuItem)sender).Parent).Tag;
            if (result)
            {
                dynamic list = datagrid.ItemsSource;
                Type type = list.GetType().GetGenericArguments()[0];
                dynamic element = Activator.CreateInstance(type);
                list.Add(element);
                list.Remove(element);
                VariableKind variableKind = ((DataSourceVisual)element).VariableKind;

                Task task = NamedObject.FindObjectByName(window.Task, Tasks);
                DataBlock dataBlock = NamedObject.FindObjectByName(window.DataBlock, task.AvailableDataBlocks);
                List<Variable> variables = dataBlock.Variables.FindAll(item => item.Kind == variableKind).ToList();
                foreach (Variable variable in variables)
                {
                    dynamic elementToAdd = Activator.CreateInstance(type);
                    elementToAdd.ProjectVersion = task.ProjectVersion;
                    elementToAdd.TaskVersion = task.TaskVersion;
                    elementToAdd.TaskName = task.Name;
                    elementToAdd.DataBlockName = dataBlock.Name;
                    elementToAdd.VariableName = variable.Name;
                    elementToAdd.VariableType = variable.Type;
                    elementToAdd.Name = variable.Name;
                    list.Add(elementToAdd);
                }

                object tag;
                dynamic selectedNode = (TreeViewItem)Tree.SelectedItem;

                Type selectedNodeTagType = selectedNode.Tag.GetType();
                if (selectedNodeTagType.IsGenericType) // Если генерируемый тип
                    if (selectedNodeTagType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)) // Если пара ключ - значение
                        tag = selectedNode.Tag.Value;
                    else
                        return;
                else // Если список
                    tag = selectedNode.Tag;

                //Родительский объект
                dynamic parentObject = tag;
                selectedNode.Items.Clear();
                Type typeOfParent = parentObject.GetType();
                PropertyInfo[] parentProperties = typeOfParent.GetProperties();
                //Перестроение родительского узла
                foreach (PropertyInfo property in parentProperties)
                {
                    type = property.PropertyType;
                    if (!type.IsGenericType && !type.IsSubclassOf(typeof(NamedObject)))
                        continue;

                    if (type.IsGenericType)
                    {
                        Type[] subTypes = type.GetGenericArguments();
                        if (type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                            RecursiveTreeBuilding(parentObject, property, subTypes[1], selectedNode);
                        else
                            RecursiveTreeBuilding(parentObject, property, subTypes[0], selectedNode);
                    }
                    else
                        RecursiveTreeBuilding(parentObject, property, type, selectedNode);
                }
                selectedNode.IsExpanded = true;

                UpdateTables();
            }
        }
        /// <summary>
        /// Сделать словарь в MultiDataSourceWithMapping общим для уровня
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void CommonDictionaryMenuItem_Click(object sender, RoutedEventArgs e)
        {
            dynamic selectedNode = (TreeViewItem)Tree.SelectedItem;

            //словарь выбранного узла
            ObservableKeyValuePair<int, Mapping> selectedNodeTag = selectedNode.Tag;
            dynamic commonDictionary = selectedNodeTag.Value.TextView;
            TreeViewItem parentNode = (TreeViewItem)selectedNode.Parent;

            //Родительский объект
            dynamic parentObject = parentNode.Tag;
            PropertyInfo analizingProperty = (PropertyInfo)((MenuItem)sender).Tag;
            //Тип рассматриваемого свойство родительского объекта
            Type typeOfAnalizingProperty = analizingProperty.PropertyType;
            //Редактируемая коллекция объектов
            dynamic parentDictionary;

            if (parentObject.GetType().IsGenericType)
            {
                parentDictionary = parentObject.Value.TextView;
                parentObject.Value.CommonDictionary = true;
            }
            else
            {
                parentDictionary = parentObject.TextView;
                parentObject.CommonDictionary = true;
            }

            foreach (var keyValuePair in parentDictionary)
            {
                ((Mapping)keyValuePair.Value).TextView = commonDictionary;
            }

            //перестроение дерева
            while (parentNode.Tag is ObservableKeyValuePair<int, Mapping>)
            {
                parentNode = (TreeViewItem)parentNode.Parent;
            }
            parentNode.Items.Clear();
            MultiDataSourceWithMapping multiDataSourceWithMapping = (MultiDataSourceWithMapping)parentNode.Tag;
            Type typeOfTextViewValue = multiDataSourceWithMapping.TextView.GetType().GetGenericArguments()[1];
            RecursiveTreeBuilding(multiDataSourceWithMapping, typeof(MultiDataSourceWithMapping).GetProperty("TextView"),
                typeOfTextViewValue, parentNode);
        }

        public void DisconnectDictionaryMenuItem_Click(object sender, RoutedEventArgs e)
        {
            dynamic selectedNode = (TreeViewItem)Tree.SelectedItem;
            dynamic selectedNodeTag = selectedNode.Tag;
            ObservableDictionary<int, Mapping> textView;
            if (selectedNodeTag.GetType() == typeof(MultiDataSourceWithMapping))
            {
                textView = selectedNodeTag.TextView;
                selectedNodeTag.CommonDictionary = false;
            }
            else
            {
                textView = selectedNodeTag.Value.TextView;
                selectedNodeTag.Value.CommonDictionary = false;
            }
            foreach (var keyValuePair in textView)
            {
                Type type = keyValuePair.Value.TextView.GetType();
                if (type == typeof(ObservableDictionary<int, Mapping>))
                {
                    ObservableDictionary<int, Mapping> commonDictionaryClone = new ObservableDictionary<int, Mapping>();

                    foreach (var elem2 in (ObservableDictionary<int, Mapping>)keyValuePair.Value.TextView)
                    {
                        commonDictionaryClone.Add(elem2.Key, (Mapping)elem2.Value.Clone());
                    }
                    commonDictionaryClone.CollectionChanged += OnTextViewCollectionChanged;
                    foreach (var item in commonDictionaryClone)
                        item.Value.PropertyChanged += UpdateMultiDataSourceWithMappingTree;
                    keyValuePair.Value.TextView = commonDictionaryClone;
                }
                else // ObservableDictionary<int, string>
                {
                    ObservableDictionary<int, string> commonDictionaryClone = new ObservableDictionary<int, string>();

                    foreach (var elem2 in (ObservableDictionary<int, string>)keyValuePair.Value.TextView)
                    {
                        commonDictionaryClone.Add(elem2.Key, elem2.Value);
                    }
                    commonDictionaryClone.CollectionChanged += OnTextViewCollectionChanged;
                    keyValuePair.Value.TextView = commonDictionaryClone;
                }
            }
        }

        private void ButtonFillFromFile_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();

            Nullable<bool> result = openFileDialog.ShowDialog();
            if (result != true)
            {
                return;
            }
            if (openFileDialog.FileName == null)
            {
                return;
            }
            System.IO.StreamReader fileStream = new System.IO.StreamReader(openFileDialog.FileName);

            ComboBox comboBox = (ComboBox)((Button)sender).Tag;
            dynamic dataSource = comboBox.SelectedItem;
            DataGrid dataGrid = (DataGrid)comboBox.Tag;
            string line;
            int tryParse;
            string key = "";
            string value1 = "";
            string value2 = "";
            string value3 = "";
            string value4 = "";
            if (dataSource.GetType() == typeof(DataSourceWithMapping<string>))
            {
                ObservableDictionary<int, string> intStringDictionary = new ObservableDictionary<int, string>();
                line = fileStream.ReadLine();  // пропускаем строку - шапку таблицы
                while ((line = fileStream.ReadLine()) != null)
                {
                    String[] lineSplit2 = line.Split(',');
                    try
                    {
                        key = lineSplit2[0].Trim();
                        value1 = lineSplit2[1].Trim();
                    }
                    catch
                    {
                        MessageBox.Show("Неверный формат файла.");
                        return;
                    }
                    if (Int32.TryParse(key, out tryParse))
                    {
                        intStringDictionary.Add(tryParse, value1);
                    }
                }
                dataGrid.ItemsSource = intStringDictionary;
                dataSource.Mapping = intStringDictionary;
            }
            else if(dataSource.GetType() == typeof(DataSourceWithMappingAndClustering))
            {
                ObservableDictionary<int, ObservableTuple2<string, string>> intTuple2Dictionary = new ObservableDictionary<int, ObservableTuple2<string, string>>();
                ObservableDictionary<string, HashSet<int>> ClusterAndCodes = new ObservableDictionary<string, HashSet<int>>();
                if (dataSource.FixClusters)
                    ClusterAndCodes = dataSource.ClusterAndCodes;

                line = fileStream.ReadLine();  // пропускаем строку - шапку таблицы
                while ((line = fileStream.ReadLine()) != null)
                {
                    String[] lineSplit2 = line.Split(',');                    
                    try
                    {
                        key = lineSplit2[0].Trim();
                        value1 = lineSplit2[1].Trim();
                        value2 = lineSplit2[2].Trim();
                    }
                    catch
                    {
                        MessageBox.Show("Неверный формат файла.");
                        return;
                    }

                    if (Int32.TryParse(key, out tryParse))
                    {
                        intTuple2Dictionary.Add(tryParse, new ObservableTuple2<string, string>(value1, value2));
                    }
                    //заполнение словаря ClusterAndCodes
                    if (ClusterAndCodes.ContainsKey(value2))
                    {
                        ClusterAndCodes[value2].Add(tryParse);
                    }
                    else
                    {
                        if (dataSource.FixClusters)  //если кластеры фиксированы и текущий кластер не содержится среди них
                        {
                            intTuple2Dictionary[tryParse].Item2 = "";
                        }
                        else
                        {
                            HashSet<int> codes = new HashSet<int>();
                            codes.Add(tryParse);
                            ClusterAndCodes.Add(value2, codes);
                        }
                    }                    
                    
                }
                dataGrid.ItemsSource = intTuple2Dictionary;
                dataSource.Mapping = intTuple2Dictionary;
                dataSource.ClusterAndCodes = ClusterAndCodes;
            }
            else if(dataSource.GetType() == typeof(DataSourceWithMappingAndColors))
            {
                ObservableDictionary<int, ObservableTuple2<string, string>> intTuple2Dictionary = new ObservableDictionary<int, ObservableTuple2<string, string>>();
                line = fileStream.ReadLine();  // пропускаем строку - шапку таблицы
                while ((line = fileStream.ReadLine()) != null)
                {
                    String[] lineSplit2 = line.Split(',');                   
                    try
                    {
                        key = lineSplit2[0].Trim();
                        value1 = lineSplit2[1].Trim();
                        value2 = lineSplit2[2].Trim(); //цвет кода
                    }
                    catch(Exception ex)
                    {
                        MessageBox.Show("Неверный формат файла." + ex.Message);
                        return;
                    }
                    // проверка правильности строкового представления цвета
                    try
                    {
                        Color color = (Color)ColorConverter.ConvertFromString((string)value2);
                    }
                    catch
                    {
                        MessageBox.Show("Неверный формат строки: " + value2);
                        return;
                    }

                    if (Int32.TryParse(key, out tryParse))
                    {
                        intTuple2Dictionary.Add(tryParse, new ObservableTuple2<string, string>(value1, value2));
                    }
                }
                dataGrid.ItemsSource = intTuple2Dictionary;
                dataSource.Mapping = intTuple2Dictionary;
            }
            else if(dataSource.GetType() == typeof(DataSourceWithMappingAndClusteringAndColors))
            {
                ObservableDictionary<int, ObservableTuple3<string, string, string>> intTuple3Dictionary = new ObservableDictionary<int, ObservableTuple3<string, string, string>>();
                ObservableDictionary<string, HashSet<int>> ClusterAndCodes = new ObservableDictionary<string, HashSet<int>>();
                ObservableDictionary<string, string> ClusterAndColor = new ObservableDictionary<string, string>();
                if (dataSource.FixClusters)
                {
                    ClusterAndCodes = dataSource.ClusterAndCodes;
                    ClusterAndColor = dataSource.ClusterAndColor;
                }
                line = fileStream.ReadLine();  // пропускаем строку - шапку таблицы
                while ((line = fileStream.ReadLine()) != null)
                {
                    String[] lineSplit2 = line.Split(',');                    
                    try
                    {
                        key = lineSplit2[0].Trim();
                        value1 = lineSplit2[1].Trim();
                        value2 = lineSplit2[2].Trim();
                        value3 = lineSplit2[3].Trim(); //цвет кластера
                        value4 = lineSplit2[4].Trim(); //цвет кода
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Неверный формат файла."+ ex.Message);
                        return;
                    }
                    // проверка правильности строкового представления цвета
                    try
                    {
                        Color color = (Color)ColorConverter.ConvertFromString((string)value3);
                    }
                    catch
                    {
                        MessageBox.Show("Неверный формат строки: " + value3);
                        return;
                    }
                    try
                    {
                        Color color = (Color)ColorConverter.ConvertFromString((string)value4);
                    }
                    catch
                    {
                        MessageBox.Show("Неверный формат строки: " + value4);
                        return;
                    }
                    if (Int32.TryParse(key, out tryParse))
                    {
                        intTuple3Dictionary.Add(tryParse, new ObservableTuple3<string, string, string>(value1, value2, value4));
                    }                   
                    //заполнение словаря ClusterAndCodes
                    if (ClusterAndCodes.ContainsKey(value2))
                    {
                        ClusterAndCodes[value2].Add(tryParse);
                    }
                    else
                    {
                        if (dataSource.FixClusters)  //если кластеры фиксированы, но текущий кластер не содержится среди них
                        {
                            intTuple3Dictionary[tryParse].Item2 = "";
                        }
                        else
                        {
                            HashSet<int> codes = new HashSet<int>();
                            codes.Add(tryParse);
                            ClusterAndCodes.Add(value2, codes);
                        }
                    }
                    //заполнение словаря ClusterAndColor
                    if (dataSource.FixClustersAndColors) //если кластеры и их цветовое представление - фиксированы
                    {
                        if (!ClusterAndColor.ContainsKey(value2))
                        {
                            intTuple3Dictionary[tryParse].Item2 = "";
                        }                        
                    }
                    else if (dataSource.FixClusters) //кластеры фиксированы, но цвета для них - нет
                    {
                        if (ClusterAndColor.ContainsKey(value2))
                        {
                            if (ClusterAndColor[value2] == "") //если цвет для кластера еще не задан
                            {
                                ClusterAndColor[value2] = value3;
                            }                            
                        }
                        else
                        {
                            intTuple3Dictionary[tryParse].Item2 = "";
                        }
                    }
                    else //если кластера не фиксированы
                    {
                        if (!ClusterAndColor.ContainsKey(value2))                       
                        {
                            ClusterAndColor.Add(value2, value3);
                        }
                    }
                    
                }
                dataGrid.ItemsSource = intTuple3Dictionary;
                dataSource.Mapping = intTuple3Dictionary;
                dataSource.ClusterAndCodes = ClusterAndCodes;                
                dataSource.ClusterAndColor = ClusterAndColor;
            }
            
            fileStream.Close();
        }

        #endregion

        #region Таблицы конфигурации
        // Устанавливает фокус на узел, при нажатии на него правой кнопки
        private void Tree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            TreeViewItem treeViewItem = VisualUpwardSearch(e.OriginalSource as DependencyObject);

            if (treeViewItem != null)
            {
                treeViewItem.Focus();
                treeViewItem.IsSelected = true;
                LastSelectedTreeViewItem = treeViewItem;
                e.Handled = true;
            }
            else
            {
                if (Tree.SelectedItem != null)
                    ((TreeViewItem)Tree.SelectedItem).IsSelected = false;
            }
        }
        //При нажатии на узел дерева левой кнопки мыши, заполняется таблица
        private void Tree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            TreeViewItem treeViewItem = VisualUpwardSearch(e.OriginalSource as DependencyObject);
            if (treeViewItem == null && Tree.SelectedItem != null) // если нажали на пустое место в TreeView, то убираем выделенный элемент
                ((TreeViewItem)Tree.SelectedItem).IsSelected = false;
            else if (treeViewItem != null)
                LastSelectedTreeViewItem = treeViewItem;

            UpdateTables();
        }
        //Поиск выбранного узла в дереве
        static TreeViewItem VisualUpwardSearch(DependencyObject source)
        {
            while (source != null && !(source is TreeViewItem))
                source = VisualTreeHelper.GetParent(source);

            return source as TreeViewItem;
        }
        //Скрытие ненужных таблиц
        private void TablesVisibility(List<Grid> grids)
        {
            foreach (Grid grid in ConfigTables.Children)
            {
                int k = Grid.GetRow(grid);
                if (grids.Contains(grid))
                {
                    grid.Visibility = Visibility.Visible;
                    ConfigTables.RowDefinitions[k].Height = new GridLength(1, GridUnitType.Auto);
                    foreach (dynamic childObject in grid.Children)
                    {
                        int n = Grid.GetRow(childObject);
                        if (childObject.GetType() == typeof(DataGrid))
                        {
                            if (childObject.ItemsSource == null)
                            {
                                childObject.Visibility = Visibility.Collapsed;
                                grid.RowDefinitions[n].Height = new GridLength(0, GridUnitType.Pixel);
                            }
                            else if (childObject.Items.Count > 20)
                            {
                                childObject.Visibility = Visibility.Visible;
                                grid.RowDefinitions[n].Height = new GridLength(400, GridUnitType.Pixel);
                                grid.RowDefinitions[n].MinHeight = 400;
                                foreach (var element in grid.Children)
                                    if (element is GridSplitter)
                                        ((GridSplitter)element).IsEnabled = true;
                            }
                            else
                            {
                                childObject.Visibility = Visibility.Visible;
                                grid.RowDefinitions[n].Height = new GridLength(1, GridUnitType.Auto);
                                foreach (var element in grid.Children)
                                    if (element is GridSplitter)
                                        ((GridSplitter)element).IsEnabled = false;

                            }
                        }
                    }
                }
                else
                {
                    grid.Visibility = Visibility.Collapsed;
                    ConfigTables.RowDefinitions[k].Height = new GridLength(0, GridUnitType.Pixel);
                }
            }
        }
        //Заполнение таблицы списком елементов типа DataSourceVisual
        private void FillTableList<T>(IEnumerable<T> list, DataGrid dataGrid)
        {
            dataGrid.ItemsSource = list;
            dataGrid.CanUserAddRows = false;
            dataGrid.CanUserResizeRows = false;
            dataGrid.Items.Refresh();
            Type typeOfElements = null;
            List<NamedObject> listDataSourceWithMapping = new List<NamedObject>();

            dynamic lastSelectedTag = LastSelectedTreeViewItem.Tag;
            Type lastSelectedTagType = lastSelectedTag.GetType();
            //Последний выбранный объект
            dynamic lastSelectedObject;
            if (lastSelectedTagType.IsGenericType) // Если генерируемый тип
                if (lastSelectedTagType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>) ||
                    lastSelectedTagType.GetGenericTypeDefinition() == typeof(ObservableKeyValuePair<,>)) // Если пара ключ - значение
                    lastSelectedObject = lastSelectedTag.Value;
                else
                    return;
            else // Если список
                lastSelectedObject = lastSelectedTag;

            if (list.Count() != 0)
            {
                if (list.GetType() == typeof(List<T>) && ((List<T>)list)[0].GetType() == lastSelectedObject.GetType() && ((List<T>)list).Contains(lastSelectedObject))
                {
                    int index = ((List<T>)list).IndexOf(lastSelectedObject);
                    dataGrid.SelectedIndex = index;
                }
                else if (list.GetType() == typeof(ObservableCollection<T>) && ((ObservableCollection<T>)list)[0].GetType() == lastSelectedObject.GetType() && ((ObservableCollection<T>)list).Contains(lastSelectedObject))
                {
                    int index = ((ObservableCollection<T>)list).IndexOf(lastSelectedObject);
                    dataGrid.SelectedIndex = index;
                }

                dynamic objectOfList = list.ElementAt(0);
                typeOfElements = objectOfList.GetType();
                //Type typeOfElements = list.GetType().GetGenericArguments()[0];

                if (typeOfElements == typeof(DataSourceVisual) || typeOfElements == typeof(DataSourceWithMapping<string>) || typeOfElements == typeof(DataSourceWithMappingAndClustering) ||
                    typeOfElements == typeof(DataSourceWithMappingAndColors) || typeOfElements == typeof(DataSourceWithMappingAndClusteringAndColors))
                {
                    listDataSourceWithMapping = list.ToList().FindAll(x => x.GetType() == typeof(DataSourceWithMapping<string>) ||
                        x.GetType() == typeof(DataSourceWithMappingAndClustering) || x.GetType() == typeof(DataSourceWithMappingAndColors) ||
                        x.GetType() == typeof(DataSourceWithMappingAndClusteringAndColors)).Select(x => x as NamedObject).ToList();
                }
                else
                {
                    PropertyInfo[] properties = typeOfElements.GetProperties();
                    listDataSourceWithMapping = list.ToList().FindAll(x => x.GetType().IsSubclassOf(typeof(DataSourceWithMapping<string>)) ||
                         x.GetType().IsSubclassOf(typeof(DataSourceWithMappingAndClustering)) || x.GetType().IsSubclassOf(typeof(DataSourceWithMappingAndColors)) ||
                         x.GetType().IsSubclassOf(typeof(DataSourceWithMappingAndClusteringAndColors))).Select(x => x as NamedObject).ToList();
                    //Добавляем столбцы доп характеристика, если они еще не были добавлены
                    if (dataGrid.Columns.Count == 6)
                    {
                        foreach (PropertyInfo property in properties)  // пробегается по свойствам текущего класса
                        {
                            string propertyName = property.Name;
                            if (property.PropertyType != typeof(string))
                                continue;

                            if ((propertyName[0] == '_') && (propertyName[1] == '_'))
                            {
                                string name;
                                var attributes = property.GetCustomAttributes(typeof(DescriptionAttribute), false);
                                if (attributes.Count() > 0)
                                {
                                    DescriptionAttribute attribute = (DescriptionAttribute)attributes[0];
                                    name = attribute.Description;
                                }
                                else
                                    name = property.Name.ToString();
                                DataGridTextColumn dataGridTextColumn = new DataGridTextColumn();
                                dataGridTextColumn.Header = name;
                                dataGridTextColumn.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                                dataGridTextColumn.Binding = new Binding(propertyName);
                                dataGrid.Columns.Add(dataGridTextColumn);
                                //сдвигаем 2 последних столбца
                                dataGrid.Columns[4].DisplayIndex = 5;
                                dataGrid.Columns[5].DisplayIndex = 6;
                                dataGridTextColumn.DisplayIndex = 4;
                            }

                        }
                    }
                }
            }

            if (dataGrid == TableImmediateListDataSource || dataGrid == TableChildObjectsListDataSource)
            {
                foreach (T element in list)
                {
                    (element as NamedObject).PropertyChanged -= UpdateNamesInListDataSourceTree;
                    (element as NamedObject).PropertyChanged += UpdateNamesInListDataSourceTree;
                }
                if (list is ObservableCollection<T>)
                {
                    (list as ObservableCollection<T>).CollectionChanged -= UpdateListDataSourceTree;
                    (list as ObservableCollection<T>).CollectionChanged += UpdateListDataSourceTree;
                }
            }


            if (list.Count() != 0 && (typeOfElements == typeof(DataSourceWithMapping<string>) || typeOfElements == typeof(DataSourceWithMappingAndClustering))
                || (listDataSourceWithMapping.Count() != 0))
            {
                if (dataGrid == TableImmediateListDataSource)
                {
                    //ComboBoxImmediateListDictionary.ItemsSource = list;
                    ComboBoxImmediateListDictionary.ItemsSource = listDataSourceWithMapping;
                    TableImmediateListWithMapping.Height = 300;
                    TableImmediateListWithMapping.Visibility = Visibility.Visible;
                }
                else if (dataGrid == TableChildObjectsListDataSource)
                {
                    //ComboBoxChildObjectsListDictionary.ItemsSource = list;
                    ComboBoxChildObjectsListDictionary.ItemsSource = listDataSourceWithMapping;
                    TableChildObjectsListWithMapping.Height = 300;
                    TableChildObjectsListWithMapping.Visibility = Visibility.Visible;
                }
                else if (dataGrid == TableImmediateDataSource || dataGrid.Name == "TableImmediateObjectsHeir")
                {

                    ComboBoxImmediateObjectsDictionary.ItemsSource = listDataSourceWithMapping;
                    TableImmediateObjectsWithMapping.Height = 300;
                    TableImmediateObjectsWithMapping.Visibility = Visibility.Visible;
                }
                else if (dataGrid == TableChildObjectsDataSource || dataGrid.Name == "TableChildObjectsHeir")
                {
                    ComboBoxChildObjectsDictionary.ItemsSource = listDataSourceWithMapping;
                    TableChildObjectsWithMapping.Height = 300;
                    TableChildObjectsWithMapping.Visibility = Visibility.Visible;
                }

            }
            else
            {
                //убираем словарь
                if (dataGrid == TableImmediateListDataSource)
                {
                    TableImmediateListWithMapping.Height = 0;
                    TableImmediateListWithMapping.Visibility = Visibility.Collapsed;
                }
                else if (dataGrid == TableChildObjectsListDataSource)
                {
                    TableChildObjectsListWithMapping.Height = 0;
                    TableChildObjectsListWithMapping.Visibility = Visibility.Collapsed;
                }
                else if (dataGrid == TableImmediateDataSource)
                {
                    TableImmediateObjectsWithMapping.Height = 0;
                    TableImmediateObjectsWithMapping.Visibility = Visibility.Collapsed;
                }
                else if (dataGrid == TableChildObjectsDataSource)
                {
                    TableChildObjectsWithMapping.Height = 0;
                    TableChildObjectsWithMapping.Visibility = Visibility.Collapsed;
                }
            }
        }

        public void UpdateTables()
        {
            TreeViewItem selectedNode = (TreeViewItem)Tree.SelectedItem;
            if (selectedNode == null)
                return;

            TableImmediateListDataSource.ItemsSource = null;
            TableChildObjectsListDataSource.ItemsSource = null;
            TableListDataSourceOfMultiDataSourceWithMapping.ItemsSource = null;
            TableDictionaryWithMapping.ItemsSource = null;
            TableImmediateObjectsDictionary.ItemsSource = null;
            TableImmediateListDictionary.ItemsSource = null;
            TableChildObjectsDictionary.ItemsSource = null;
            TableChildObjectsListDictionary.ItemsSource = null;
            ComboBoxChildObjects.ItemsSource = null;
            ComboBoxImmediateObjectsDictionary.ItemsSource = null;
            ComboBoxImmediateListDictionary.ItemsSource = null;
            ComboBoxChildObjectsDictionary.ItemsSource = null;
            ComboBoxChildObjectsListDictionary.ItemsSource = null;

            //
            //добавить для новых созданных таблиц
            //
            ComboBoxImmediateList.ItemsSource = null;

            List<Grid> visibleTables = new List<Grid>();
            List<DataSourceVisual> immediateDataSourceList = new List<DataSourceVisual>();
            List<dynamic> immediateDataSourceHeirList = new List<dynamic>();
            List<PropertyInfo> listDataSourceProperty = new List<PropertyInfo>();
            List<dynamic> listChildObjects = new List<dynamic>();

            //Тэг выбранного узла
            dynamic selectedNodeTag = selectedNode.Tag;
            //Тип тэга выбранного узла
            Type selectedNodeTagType = selectedNodeTag.GetType();
            //Выбранный объект
            dynamic selectedObject;

            if (selectedNodeTagType.IsGenericType && (selectedNodeTagType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>) ||
                   selectedNodeTagType.GetGenericTypeDefinition() == typeof(ObservableKeyValuePair<,>))) // Если пара ключ - значение
                selectedObject = selectedNodeTag.Value;
            else // Если список
                selectedObject = selectedNodeTag;
            //Тип выбранного объекта
            Type selectedObjectType = selectedObject.GetType();
            //Имя типа выбранного объекта
            string selectedObjectTypeString = selectedObjectType.Name;
            //Если выбранные объект DataSourceVisual или его наследник
            if (selectedObjectType == typeof(DataSourceVisual) || selectedObjectType.IsSubclassOf(typeof(DataSourceVisual)))
            {
                dynamic parentNode = ((TreeViewItem)Tree.SelectedItem).Parent;
                dynamic parentNodeTag = parentNode.Tag;
                Type parentNodeTagType = parentNodeTag.GetType();
                //Выбранный объект
                dynamic parentObject;
                if (parentNodeTagType.IsGenericType) // Если генерируемый тип
                    if (parentNodeTagType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>) ||
                        parentNodeTagType.GetGenericTypeDefinition() == typeof(ObservableKeyValuePair<,>)) // Если пара ключ - значение
                        parentObject = parentNodeTag.Value;
                    else
                        return;
                else // Если список
                    parentObject = parentNodeTag;
                Type parentType = parentObject.GetType();
                PropertyInfo[] parentProperties = parentType.GetProperties();
                // пробегается по свойствам класса родителя
                foreach (PropertyInfo property in parentProperties)
                {
                    Type propertyType = property.PropertyType;
                    if (!propertyType.IsGenericType || propertyType == typeof(DataSourceWithMapping<string>))
                        continue;

                    Type[] subTypes = propertyType.GetGenericArguments();
                    Type nextType;
                    dynamic list;
                    if (propertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                    {
                        nextType = subTypes[1];
                        list = Activator.CreateInstance(typeof(List<>).MakeGenericType(nextType));
                        foreach (dynamic keyValuePair in property.GetValue(parentObject))
                            list.Add(keyValuePair.Value);
                    }
                    else
                    {
                        nextType = subTypes[0];
                        list = property.GetValue(parentObject);
                    }
                    //Полученное свойство родителя не то, что мы сейчас редактируем
                    //if (nextType != selectedObject.GetType())
                    //    continue;

                    bool desiredProperty = false;
                    foreach (dynamic item in list)
                    {
                        if (object.ReferenceEquals(item, selectedObject))
                        {
                            desiredProperty = true;
                            continue;
                        }
                    }
                    if (!desiredProperty)
                        continue;

                    if (list.Count > 0)
                    {
                        FillTableList(list, TableImmediateListDataSource);
                        visibleTables.Add(TableImmediateList);
                    }
                }

            }
            //Выбранный объект не отображается в родителе
            else if (selectedObjectTypeString[0] != '_')
            {
                //если нажали на MultiDataSourceWithMapping
                if (selectedObject.GetType() == typeof(MultiDataSourceWithMapping))
                {
                    TableListDataSourceOfMultiDataSourceWithMapping.CanUserAddRows =
                            selectedObject.NumDataSources <= 0;
                    TableListDataSourceOfMultiDataSourceWithMapping.CanUserDeleteRows =
                        selectedObject.NumDataSources <= 0;

                    //TableDictionaryWithMapping.Tag = multiDataSourceWithMapping;
                    TableListDataSourceOfMultiDataSourceWithMapping.ItemsSource = selectedObject.DataSources;
                    dynamic textView = selectedObject.TextView;
                    if (textView.GetType() == typeof(ObservableDictionary<int, string>))
                        ColumnValueDictionaryWithMapping.Binding = new Binding("Value")
                        {
                            Mode = BindingMode.TwoWay
                        };
                    else
                    {
                        ColumnValueDictionaryWithMapping.Binding = new Binding("Value.Name")
                        {
                            Mode = BindingMode.TwoWay
                        };
                        ((ObservableDictionary<int, Mapping>)selectedObject.TextView).CollectionChanged
                            -= OnTextViewCollectionChanged;
                        ((ObservableDictionary<int, Mapping>)selectedObject.TextView).CollectionChanged
                            += OnTextViewCollectionChanged;
                    }
                    ((MultiDataSourceWithMapping)selectedObject).DataSources.CollectionChanged -= OnDataSourcesCollectionChanged;
                    ((MultiDataSourceWithMapping)selectedObject).DataSources.CollectionChanged += OnDataSourcesCollectionChanged;
                    TableDictionaryWithMapping.ItemsSource = textView;
                    visibleTables.Add(TableImmediateMultiDataSourceWithMapping);
                }
                else
                {
                    PropertyInfo[] properties = selectedObject.GetType().GetProperties();
                    foreach (PropertyInfo property in properties)  // пробегается по свойствам текущего класса
                    {
                        Type propertyType = property.PropertyType;
                        //Анализ полей типа MultiDataSourceWithMapping
                        if (propertyType == typeof(MultiDataSourceWithMapping))
                        {
                            MultiDataSourceWithMapping multiDataSourceWithMapping
                                = (MultiDataSourceWithMapping)property.GetValue(selectedObject);
                            TableListDataSourceOfMultiDataSourceWithMapping.CanUserAddRows =
                                multiDataSourceWithMapping.NumDataSources <= 0;
                            TableListDataSourceOfMultiDataSourceWithMapping.CanUserDeleteRows =
                                multiDataSourceWithMapping.NumDataSources <= 0;

                            //TableDictionaryWithMapping.Tag = multiDataSourceWithMapping;
                            TableListDataSourceOfMultiDataSourceWithMapping.ItemsSource = multiDataSourceWithMapping.DataSources;
                            dynamic textView = multiDataSourceWithMapping.TextView;
                            if (textView.GetType() == typeof(ObservableDictionary<int, string>))
                                ColumnValueDictionaryWithMapping.Binding = new Binding("Value")
                                {
                                    Mode = BindingMode.TwoWay
                                };
                            else
                            {
                                ColumnValueDictionaryWithMapping.Binding = new Binding("Value.Name")
                                {
                                    Mode = BindingMode.TwoWay
                                };
                                ((ObservableDictionary<int, Mapping>)multiDataSourceWithMapping.TextView).CollectionChanged
                                    -= OnTextViewCollectionChanged;
                                ((ObservableDictionary<int, Mapping>)multiDataSourceWithMapping.TextView).CollectionChanged
                                    += OnTextViewCollectionChanged;
                            }
                            multiDataSourceWithMapping.DataSources.CollectionChanged -= OnDataSourcesCollectionChanged;
                            multiDataSourceWithMapping.DataSources.CollectionChanged += OnDataSourcesCollectionChanged;
                            TableDictionaryWithMapping.ItemsSource = textView;
                            visibleTables.Add(TableImmediateMultiDataSourceWithMapping);
                        }
                        //Анализ полей типа Mapping
                        else if (propertyType == typeof(object) && selectedObject.GetType() == typeof(Mapping))
                        {
                            Mapping mapping = (Mapping)selectedObject;
                            dynamic textView = property.GetValue(selectedObject);
                            if (textView.GetType() == typeof(ObservableDictionary<int, string>))
                            {
                                ColumnValueDictionaryWithMapping.Binding = new Binding("Value")
                                {
                                    Mode = BindingMode.TwoWay
                                };
                                ObservableDictionary<int, string> dictionary = (ObservableDictionary<int, string>)textView;
                            }
                            else
                            {
                                ColumnValueDictionaryWithMapping.Binding = new Binding("Value.Name")
                                {
                                    Mode = BindingMode.TwoWay
                                };
                                ObservableDictionary<int, Mapping> dictionary = (ObservableDictionary<int, Mapping>)textView;
                                dictionary.CollectionChanged -= OnTextViewCollectionChanged;
                                dictionary.CollectionChanged += OnTextViewCollectionChanged;
                            }
                            TableDictionaryWithMapping.ItemsSource = textView;
                            visibleTables.Add(TableImmediateMultiDataSourceWithMapping);
                        }
                        else
                        {
                            //Поиск непосредственных полей типа DataSourceVisual и DataSourceWithMapping
                            if (propertyType == typeof(DataSourceWithMapping<string>) || propertyType == typeof(DataSourceVisual) || propertyType == typeof(DataSourceWithMappingAndClustering) ||
                                 propertyType == typeof(DataSourceWithMappingAndColors) || propertyType == typeof(DataSourceWithMappingAndClusteringAndColors))
                            {
                                immediateDataSourceList.Add(property.GetValue(selectedObject));
                                continue;
                            }
                            if (propertyType.IsSubclassOf(typeof(DataSourceVisual)))
                            {
                                immediateDataSourceHeirList.Add(property.GetValue(selectedObject));
                                continue;
                            }

                            if (!propertyType.IsGenericType)
                                continue;

                            Type[] subTypes = propertyType.GetGenericArguments();
                            Type nextType;
                            dynamic list;
                            if (propertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>)) // Словарь
                            {
                                nextType = subTypes[1];
                                list = Activator.CreateInstance(typeof(List<>).MakeGenericType(nextType));
                                foreach (dynamic keyValuePair in property.GetValue(selectedObject))
                                {
                                    list.Add(keyValuePair.Value);
                                }
                            }
                            else if (propertyType.GetGenericTypeDefinition() == typeof(List<>) ||
                                    propertyType.GetGenericTypeDefinition() == typeof(ObservableCollection<>))//Список
                            {
                                nextType = subTypes[0];
                                list = property.GetValue(selectedObject);
                            }
                            else
                                continue;

                            if ((nextType == typeof(DataSourceVisual) || nextType == typeof(DataSourceWithMapping<string>) ||
                                nextType.IsSubclassOf(typeof(DataSourceVisual))) && (list.Count > 0))
                            {
                                listDataSourceProperty.Add(property);
                                if (listDataSourceProperty.Count > 1)
                                    ComboBoxImmediateList.Tag = selectedObject;
                                else
                                {
                                    FillTableList(list, TableImmediateListDataSource);
                                    visibleTables.Add(TableImmediateList);
                                }
                            }
                            // Список дочерних объектов, которые являются наследниками NamedObject и должны отображаться в родителе
                            else if (nextType.Name[0] == '_' && nextType.Name[1] != '_')
                            {
                                PropertyInfo[] nextTypeProperties = nextType.GetProperties();
                                listChildObjects.AddRange(list);
                                ComboBoxChildObjects.ItemsSource = listChildObjects;
                                ComboBoxChildObjects.Items.Refresh();
                                if (list.Count > 0)
                                {
                                    //проверяем содержит ли дочерний объект непосредственные поля DataSourceVisual                                
                                    bool immediateObjectsOfNextType = nextTypeProperties.ToList().FindAll(item => IsImmediateDataSource(item.PropertyType)).Count > 0;
                                    //проверяем содержит ли дочерний объект непосредственные поля - наследники DataSourceVisual
                                    bool immediateObjectsHeirOfNextType = nextTypeProperties.ToList().FindAll(item => IsDataSourceHeir(item.PropertyType)).Count > 0;

                                    if (immediateObjectsOfNextType || immediateObjectsHeirOfNextType)
                                    {
                                        visibleTables.Add(TableChildObjects);
                                        visibleTables.Add(ComboBoxGrid);
                                    }
                                    ComboBoxChildObjects.SelectedItem = listChildObjects[0];
                                }
                            }
                        }
                    }
                }

            }
            //Выбранный объект должен отображаться в родительском объекте
            else if ((selectedObjectTypeString[0] == '_') && (selectedObjectTypeString[1] != '_'))
            {
                dynamic parentNode = ((TreeViewItem)Tree.SelectedItem).Parent;
                dynamic parentNodeTag = parentNode.Tag;
                Type parentNodeTagType = parentNodeTag.GetType();
                //Выбранный объект
                dynamic parentObject;
                if (parentNodeTagType.IsGenericType) // Если генерируемый тип
                    if (parentNodeTagType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>) ||
                        parentNodeTagType.GetGenericTypeDefinition() == typeof(ObservableKeyValuePair<,>)) // Если пара ключ - значение
                        parentObject = parentNodeTag.Value;
                    else
                        return;
                else // Если список
                    parentObject = parentNodeTag;
                Type parentType = parentObject.GetType();
                PropertyInfo[] parentProperties = parentType.GetProperties();
                // пробегается по свойствам класса родителя
                foreach (PropertyInfo property in parentProperties)
                {
                    Type propertyType = property.PropertyType;
                    if (!propertyType.IsGenericType)
                        continue;

                    Type[] subTypes = propertyType.GetGenericArguments();
                    Type nextType;
                    dynamic list;
                    if (propertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                    {
                        nextType = subTypes[1];
                        list = Activator.CreateInstance(typeof(List<>).MakeGenericType(nextType));
                        foreach (dynamic keyValuePair in property.GetValue(parentObject))
                            list.Add(keyValuePair.Value);
                    }
                    else
                    {
                        nextType = subTypes[0];
                        list = property.GetValue(parentObject);
                    }
                    //Полученное свойство родителя не то, что мы сейчас редактируем
                    if (nextType != selectedObject.GetType())
                        continue;

                    PropertyInfo[] properties = selectedObject.GetType().GetProperties();
                    ComboBoxChildObjects.ItemsSource = list;
                    ComboBoxChildObjects.Items.Refresh();
                    //ComboBoxChildObjects.SelectedItem = selectedObject;
                    //проверяем содержит ли выбранный объект непосредственные поля DataSourceVisual
                    bool immediateObjectsOfNextType = properties.ToList().FindAll(item => IsImmediateDataSource(item.PropertyType)).Count > 0;
                    //проверяем содержит ли выбранный объект непосредственные поля - наследники DataSourceVisual
                    bool immediateObjectsHeirOfNextType = properties.ToList().FindAll(item => IsDataSourceHeir(item.PropertyType)).Count > 0;

                    if (immediateObjectsOfNextType || immediateObjectsHeirOfNextType)
                    {
                        visibleTables.Add(TableChildObjects);
                    }
                    //проверяем содержит ли выбранный объект непосредственные поля типа List<:DataSourceVisual>
                    List<PropertyInfo> propertyInfoList = properties.ToList().FindAll(item =>
                        (item.PropertyType.IsGenericType &&
                        (item.PropertyType.GetGenericTypeDefinition() == typeof(List<>)
                        || item.PropertyType.GetGenericTypeDefinition() == typeof(ObservableCollection<>))
                        && (item.PropertyType.GetGenericArguments()[0] == typeof(DataSourceVisual) ||
                            item.PropertyType.GetGenericArguments()[0].IsSubclassOf(typeof(DataSourceVisual)))
                        ));
                    bool immediateObjectsListOfNextType = propertyInfoList.Count > 0;

                    if (immediateObjectsListOfNextType)
                    {
                        if (propertyInfoList.Count > 1)
                            visibleTables.Add(ComboBoxChildObjectsDataSourceListGrid);
                        visibleTables.Add(TableChildObjectsList);
                    }
                    if (immediateObjectsOfNextType || immediateObjectsHeirOfNextType || immediateObjectsListOfNextType)
                    {
                        visibleTables.Add(ComboBoxGrid);
                    }
                    ComboBoxChildObjects.SelectedItem = selectedObject;
                }
            }
            if (immediateDataSourceList.Count != 0)
            {
                FillTableList(immediateDataSourceList, TableImmediateDataSource);
                visibleTables.Add(TableImmediateObjects);
            }
            if (immediateDataSourceHeirList.Count != 0)
            {
                CreateTables(immediateDataSourceHeirList, TableImmediateObjects);
                if (!visibleTables.Contains(TableImmediateObjects))
                    visibleTables.Add(TableImmediateObjects);
            }
            if (listDataSourceProperty.Count > 1)
            {
                ComboBoxImmediateList.ItemsSource = listDataSourceProperty;
                ComboBoxImmediateList.SelectedItem = listDataSourceProperty[0];
                visibleTables.Add(ComboBoxDataSourceListGrid);
                visibleTables.Add(TableImmediateList);
            }
            TablesVisibility(visibleTables);
        }

        public void CreateTables<T>(List<T> dataSourceHeirs, Grid grid)
        {

            if (grid.RowDefinitions.Count != dataSourceHeirs.Count + 3)
            {
                for (int i = 0; i < dataSourceHeirs.Count; i++)
                {
                    dynamic dataSourceHeir = dataSourceHeirs[i];

                    DataGrid TableImmediateDataSourceHeir = new DataGrid();
                    TableImmediateDataSourceHeir.AutoGenerateColumns = false;
                    TableImmediateDataSourceHeir.Name = grid.Name + "Heir";

                    grid.RowDefinitions.Add(new RowDefinition());
                    grid.RowDefinitions[i + 1].Height = new GridLength(200, GridUnitType.Pixel);
                    grid.Children.Add(TableImmediateDataSourceHeir);
                    Grid.SetRow(TableImmediateDataSourceHeir, i + 1);
                    TableImmediateDataSourceHeir.Columns.Add(new DataGridTextColumn());
                    TableImmediateDataSourceHeir.Columns[0].Header = "Имя";
                    DataGridTextColumn column = (DataGridTextColumn)TableImmediateDataSourceHeir.Columns[0];
                    column.Binding = new Binding("Name");
                    column.IsReadOnly = true;
                    TableImmediateDataSourceHeir.Columns.Add((DataGridTemplateColumn)this.TryFindResource("TemplateColumnTask"));
                    TableImmediateDataSourceHeir.Columns.Add((DataGridTemplateColumn)this.TryFindResource("TemplateColumnDataBlock"));
                    TableImmediateDataSourceHeir.Columns.Add((DataGridTemplateColumn)this.TryFindResource("TemplateColumnVariable"));
                    TableImmediateDataSourceHeir.Columns.Add((DataGridTemplateColumn)this.TryFindResource("TemplateColumnIsEnable"));
                    TableImmediateDataSourceHeir.Columns.Add((DataGridTemplateColumn)this.TryFindResource("TemplateColumnNumberConversions"));

                    Type dataSourceHeirType = dataSourceHeir.GetType();
                    if (!dataSourceHeirType.IsGenericType)
                    {
                        List<dynamic> dataSourceHeirList = new List<dynamic>();
                        dataSourceHeirList.Add(dataSourceHeir);
                        FillTableList(dataSourceHeirList, TableImmediateDataSourceHeir);
                    }
                    else
                        FillTableList(dataSourceHeir, TableImmediateDataSourceHeir);

                }
                int rowCount = grid.RowDefinitions.Count;
                grid.RowDefinitions[rowCount - 2].Height = new GridLength(8, GridUnitType.Pixel);
                if (grid.Name == "TableImmediateObjects")
                    Grid.SetRow(GridSplitterImmediateObjects, rowCount - 2);
                else if (grid.Name == "TableChildObjects")
                    Grid.SetRow(GridSplitterChildObjects, rowCount - 2);
                grid.RowDefinitions[rowCount - 1].Height = new GridLength(1, GridUnitType.Auto);
                if (grid.Name == "TableImmediateObjects")
                    Grid.SetRow(TableImmediateObjectsWithMapping, rowCount - 1);
                else if (grid.Name == "TableChildObjects")
                    Grid.SetRow(TableChildObjectsWithMapping, rowCount - 1);
            }

        }

        //Была выбрана новая переменная, нужно задать тип выбранной переменной
        private void Variable_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox comboBox = (ComboBox)sender;
            DataSourceVisual dataSourceVisual = (DataSourceVisual)comboBox.DataContext;
            if (string.IsNullOrEmpty(dataSourceVisual.VariableName))
                return;
            Task task = NamedObject.FindObjectByName(dataSourceVisual.TaskName, Tasks);
            DataBlock dataBlock = NamedObject.FindObjectByName(dataSourceVisual.DataBlockName, task.AvailableDataBlocks);
            Variable variable = dataBlock.Variables.Find(item => item.Name == dataSourceVisual.VariableName);
            dataSourceVisual.VariableType = variable.Type;
        }

        //Вызывает для задания определенных преобразований над переменной
        private void NumberConversions_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            DataSourceVisual dataSourceVisual = (DataSourceVisual)button.DataContext;
            int typeSize = dataSourceVisual.Size;
            string message = "Битовые преобразования заданные ранее неприменимы к выбранному типу данных!\n" +
                "Выберете другую переменную, чтобы сохранить преобразования. Если вы решите продолжить они будут утеряны!\nПродолжить?";
            if (typeSize * 8 != dataSourceVisual.BitsConversion.Count)
                if (MessageBox.Show(message, "Преобразование переменной", MessageBoxButton.YesNo) == MessageBoxResult.No)
                    return;
                else
                    dataSourceVisual.BitsConversion.Clear();
            NumberConversions window = new NumberConversions(dataSourceVisual, typeSize);
            window.ShowDialog();
        }

        //Выбор цвета для текстового представления
        private void ColorSelectButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            dynamic keyValuePair = button.DataContext;
            if (!(keyValuePair.GetType().IsGenericType))
                return;
            Type type = keyValuePair.GetType().GetGenericArguments()[1];
            UcColorSelector ColorPanel = new UcColorSelector(button.Background);

            if (ColorPanel.ShowDialog() == true)
            {
                if (type == typeof(ObservableTuple2<string, string>))
                {
                    ((ObservableKeyValuePair<int, ObservableTuple2<string, string>>)keyValuePair).Value.Item2 = Convert.ToString(ColorPanel.color);
                }
                else if (type == typeof(ObservableTuple3<string, string, string>))
                {
                    ((ObservableKeyValuePair<int, ObservableTuple3<string, string, string>>)keyValuePair).Value.Item3 = Convert.ToString(ColorPanel.color);
                }
            }
        }

        #endregion

        #region Обработчики событий елементов ComboBox
        private void ComboBoxDictionary_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox comboBox = (ComboBox)sender;
            dynamic item = comboBox.SelectedItem;
            DataGrid dataGridDictionary = (DataGrid)comboBox.Tag;
            if (item == null)
                return;
            dataGridDictionary.ItemsSource = item.Mapping;
            if (item.GetType() == typeof(DataSourceWithMappingAndColors) || item.GetType().IsSubclassOf(typeof(DataSourceWithMappingAndColors)))
            {
                dataGridDictionary.Columns[2].Visibility = Visibility.Collapsed;
                ((DataGridTextColumn)dataGridDictionary.Columns[1]).Binding = new Binding("Value.Item1");
                if (dataGridDictionary.Columns.Count > 3)
                    dataGridDictionary.Columns.RemoveAt(3);
                dataGridDictionary.Columns.Add((DataGridTemplateColumn)this.TryFindResource("ColumnColorItem2"));
            }
            else if (item.GetType() == typeof(DataSourceWithMappingAndClusteringAndColors) || item.GetType().IsSubclassOf(typeof(DataSourceWithMappingAndClusteringAndColors)))
            {
                dataGridDictionary.Columns[2].Visibility = Visibility.Visible;
                ((DataGridTextColumn)dataGridDictionary.Columns[1]).Binding = new Binding("Value.Item1");
                if (dataGridDictionary.Columns.Count > 3)
                    dataGridDictionary.Columns.RemoveAt(3);
                dataGridDictionary.Columns.Add((DataGridTemplateColumn)this.TryFindResource("ColumnColorItem3"));
            }
            else if (item.GetType() == typeof(DataSourceWithMappingAndClustering) || item.GetType().IsSubclassOf(typeof(DataSourceWithMappingAndClustering)))
            {
                dataGridDictionary.Columns[2].Visibility = Visibility.Visible;
                dataGridDictionary.Columns[3].Visibility = Visibility.Collapsed;
                ((DataGridTextColumn)dataGridDictionary.Columns[1]).Binding = new Binding("Value.Item1");
            }
            else
            {
                dataGridDictionary.Columns[2].Visibility = Visibility.Collapsed;
                dataGridDictionary.Columns[3].Visibility = Visibility.Collapsed;
                ((DataGridTextColumn)dataGridDictionary.Columns[1]).Binding = new Binding("Value");
            }
        }

        private void ComboBoxChildObject_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox comboBox = (ComboBox)sender;
            dynamic item = comboBox.SelectedItem;
            if (item != null)
            {
                Type itemType = item.GetType();
                PropertyInfo[] properties = itemType.GetProperties();
                List<DataSourceVisual> immediateDataSourceList = new List<DataSourceVisual>();
                List<dynamic> immediateDataSourceHeirList = new List<dynamic>();
                List<PropertyInfo> listDataSourceProperty = new List<PropertyInfo>();
                foreach (PropertyInfo property in properties)  // пробегается по свойствам текущего класса
                {
                    Type propertyType = property.PropertyType;
                    //Поиск непосредственных полей типа DataSourceVisual и DataSourceWithMapping
                    if (propertyType == typeof(DataSourceWithMapping<string>) || propertyType == typeof(DataSourceVisual) ||
                        propertyType == typeof(DataSourceWithMappingAndClustering) || propertyType == typeof(DataSourceWithMappingAndColors) ||
                        propertyType == typeof(DataSourceWithMappingAndClusteringAndColors))
                    {
                        immediateDataSourceList.Add(property.GetValue(item));
                        continue;
                    }
                    if (propertyType.IsSubclassOf(typeof(DataSourceVisual)))
                    {
                        immediateDataSourceHeirList.Add(property.GetValue(item));
                        continue;
                    }
                    if (!propertyType.IsGenericType)
                        continue;

                    Type[] subTypes = propertyType.GetGenericArguments();
                    Type nextType;
                    dynamic list;
                    if (propertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                    {
                        nextType = subTypes[1];
                        list = Activator.CreateInstance(typeof(List<>).MakeGenericType(nextType));
                        foreach (dynamic keyValuePair in property.GetValue(item))
                        {
                            list.Add(keyValuePair.Value);
                        }
                    }
                    else
                    {
                        nextType = subTypes[0];
                        list = property.GetValue(item);
                    }

                    if (nextType.IsSubclassOf(typeof(DataSourceVisual)) || nextType == typeof(DataSourceVisual))
                    {
                        listDataSourceProperty.Add(property);
                        if (listDataSourceProperty.Count > 1)
                            ComboBoxChildObjectsDataSourceList.Tag = item;
                        else if (list.Count > 0)
                        {
                            FillTableList(list, TableChildObjectsListDataSource);
                        }
                    }

                }
                if (immediateDataSourceList.Count != 0)
                {
                    FillTableList(immediateDataSourceList, TableChildObjectsDataSource);
                }
                if (immediateDataSourceHeirList.Count != 0)
                {
                    CreateTables(immediateDataSourceHeirList, TableChildObjects);
                }
                if (listDataSourceProperty.Count > 1)
                {
                    ComboBoxChildObjectsDataSourceList.ItemsSource = listDataSourceProperty;
                    ComboBoxChildObjectsDataSourceList.SelectedItem = null;
                    ComboBoxChildObjectsDataSourceList.SelectedItem = listDataSourceProperty[0];
                }
            }
        }

        private void ComboBoxImmediateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox comboBox = (ComboBox)sender;
            PropertyInfo property = (PropertyInfo)comboBox.SelectedItem; //свойство - список DataSourceVisual или наследников DataSourceVisual
            if (property != null)
            {
                object parentTag = ComboBoxImmediateList.Tag;
                dynamic parentObject = parentTag;
                dynamic list;
                list = property.GetValue(parentObject);
                FillTableList(list, TableImmediateListDataSource);
            }
        }

        private void ComboBoxChildObjectsDataSourceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox comboBox = (ComboBox)sender;
            PropertyInfo property = (PropertyInfo)comboBox.SelectedItem; //свойство - список DataSourceVisual или наследников DataSourceVisual
            if (property != null)
            {
                object parentTag = ComboBoxChildObjectsDataSourceList.Tag;
                dynamic parentObject = parentTag;
                dynamic list;
                list = property.GetValue(parentObject);
                FillTableList(list, TableChildObjectsListDataSource);

                int k = Grid.GetRow(TableChildObjectsList);

                TableChildObjectsList.Visibility = Visibility.Visible;
                ConfigTables.RowDefinitions[k].Height = new GridLength(1, GridUnitType.Auto);

                int n = Grid.GetRow(TableChildObjectsListDataSource);

                if (TableChildObjectsListDataSource.Items.Count > 20)
                {
                    TableChildObjectsListDataSource.Visibility = Visibility.Visible;
                    TableChildObjectsList.RowDefinitions[n].Height = new GridLength(400, GridUnitType.Pixel);
                }
                else
                {
                    TableChildObjectsListDataSource.Visibility = Visibility.Visible;
                    TableChildObjectsList.RowDefinitions[n].Height = new GridLength(1, GridUnitType.Auto);
                }
            }
        }

        #endregion

        #region Взаимодействие с DataGrid
        private void DataGridDataSource_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            if (e.AddedCells.Count != 1) return;
            DependencyObject cell = (DependencyObject)sender;
            while (cell != null && !(cell is DataGrid))
                cell = VisualTreeHelper.GetParent(cell);
            DataGrid dataGrid = (DataGrid)cell;
            int index = dataGrid.Items.IndexOf(dataGrid.SelectedCells[0].Item);
            if (!dataGrid.CanUserAddRows || index != dataGrid.Items.Count - 1 )
            {
                dataGrid.CommitEdit();
                dataGrid.BeginEdit();
            }
            
        }
        #endregion

        #region Взаимодествие с объектами типа MultiDataSourceWithMapping
        //Обновление дерева в случае удаления или добавления элементов в коллекцию TextView
        //объекта типа MultiDataSourceWithMapping
        void OnTextViewCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            dynamic dictionary = sender;
            //Достаем MultiDataSourceWithMapping, с которым раотаем в текущий момент
            TreeViewItem multiDataSourceTreeViewItem = (TreeViewItem)TableDictionaryWithMapping.Tag;
            MultiDataSourceWithMapping multiDataSourceWithMapping = (MultiDataSourceWithMapping)multiDataSourceTreeViewItem.Tag;
            //Определяем уровень дерева, на которм в текущий момент находимся
            int level = 1;
            TreeViewItem parentNode = (TreeViewItem)Tree.SelectedItem;
            if (!(parentNode.Tag is ObservableKeyValuePair<int, Mapping>) && !(parentNode.Tag is MultiDataSourceWithMapping))
                parentNode = multiDataSourceTreeViewItem;
            while (parentNode.Tag is ObservableKeyValuePair<int, Mapping>)
            {
                parentNode = (TreeViewItem)parentNode.Parent;
                level++;
            }
            //Отвязываем от удаленных элементов метод для вызова при изменении свойства Name
            if (e.OldItems != null && e.OldItems[0].GetType() == typeof(ObservableKeyValuePair<int, Mapping>))
            {
                foreach (ObservableKeyValuePair<int, Mapping> item in e.OldItems)
                    item.Value.PropertyChanged -= UpdateMultiDataSourceWithMappingTree;
            }
            //Привязываем к добавленным элементам метод для вызова при изменении свойства Name
            if (e.NewItems != null && e.NewItems[0].GetType() == typeof(ObservableKeyValuePair<int, Mapping>))
            {
                foreach (ObservableKeyValuePair<int, Mapping> item in e.NewItems)
                    item.Value.PropertyChanged += UpdateMultiDataSourceWithMappingTree;
            }
            //Заполняем свойство TextView для новых элементов типа Mapping
            if (e.NewItems != null)
            {
                dynamic item = e.NewItems[0];
                if (level < multiDataSourceWithMapping.DataSources.Count - 1 && item.Value != null)
                {
                    var dict = new ObservableDictionary<int, Mapping>();
                    item.Value.TextView = dict;
                    dict.CollectionChanged += OnTextViewCollectionChanged;
                }
                else if (level == multiDataSourceWithMapping.DataSources.Count - 1 && item.Value != null)
                {
                    var dict = new ObservableDictionary<int, string>();
                    item.Value.TextView = dict;
                    dict.CollectionChanged += OnTextViewCollectionChanged;
                }
            }
            //Очищаем и перестраиваем узел MultiDataSourceWithMapping
            parentNode.Items.Clear();

            Type typeOfTextViewValue = multiDataSourceWithMapping.TextView.GetType().GetGenericArguments()[1];
            RecursiveTreeBuilding(multiDataSourceWithMapping, typeof(MultiDataSourceWithMapping).GetProperty("TextView"),
                typeOfTextViewValue, parentNode);
        }

        //Обновление дерева в случае изменения имен объектов в коллекции TextView
        //объекта типа MultiDataSourceWithMapping
        public void UpdateMultiDataSourceWithMappingTree(object sender, PropertyChangedEventArgs e)
        {
            //Достаем MultiDataSourceWithMapping, с которым раотаем в текущий момент
            TreeViewItem multiDataSourceTreeViewItem = (TreeViewItem)TableDictionaryWithMapping.Tag;
            MultiDataSourceWithMapping multiDataSourceWithMapping = (MultiDataSourceWithMapping)multiDataSourceTreeViewItem.Tag;
            dynamic dictionary = multiDataSourceWithMapping.TextView;
            multiDataSourceTreeViewItem.Items.Clear();
            RecursiveTreeBuilding(multiDataSourceWithMapping, typeof(MultiDataSourceWithMapping).GetProperty("TextView"),
                dictionary.GetType().GetGenericArguments()[1], multiDataSourceTreeViewItem);
        }

        //Обработчик события произошли изменения в ObservableList<DataSourceVisual> DataSources
        //объекта типа MultiDataSourceWithMapping
        void OnDataSourcesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            ObservableCollection<DataSourceVisual> collection = (ObservableCollection<DataSourceVisual>)sender;
            //Отвязать все старые привязки на изменение коллекций!!!
            TreeViewItem multiDataSourceTreeViewItem = (TreeViewItem)TableDictionaryWithMapping.Tag;
            MultiDataSourceWithMapping multiDataSourceWithMapping = (MultiDataSourceWithMapping)multiDataSourceTreeViewItem.Tag;
            //Если список DataSources пуст то формирование дерева дешифрации невозможно
            TableDictionaryWithMapping.IsEnabled = collection.Count != 0;
            if (collection.Count == 0)
            {
                multiDataSourceWithMapping.TextView = null;
                TableDictionaryWithMapping.ItemsSource = null;
                return;
            }

            //Если в списке DataSources больше одного элемента
            if (collection.Count > 1)
            {
                var dict = new ObservableDictionary<int, Mapping>();
                dict.CollectionChanged += OnTextViewCollectionChanged;
                multiDataSourceWithMapping.TextView = dict;
                TableDictionaryWithMapping.ItemsSource = null;
                ColumnValueDictionaryWithMapping.Binding = new Binding("Value.Name")
                {
                    Mode = BindingMode.TwoWay
                };
            }//Если в списке DataSources ровно один элемента
            else
            {
                var dict = new ObservableDictionary<int, string>();
                dict.CollectionChanged += OnTextViewCollectionChanged;
                //multiDataSourceWithMapping.TextView = dict;
                TableDictionaryWithMapping.ItemsSource = null;
                ColumnValueDictionaryWithMapping.Binding = new Binding("Value")
                {
                    Mode = BindingMode.TwoWay
                };
            }
            dynamic dictionary = multiDataSourceWithMapping.TextView;
            TableDictionaryWithMapping.ItemsSource = dictionary;

            multiDataSourceTreeViewItem.Items.Clear();
            Type typeOfTextViewValue = multiDataSourceWithMapping.TextView.GetType().GetGenericArguments()[1];
            RecursiveTreeBuilding(multiDataSourceWithMapping, typeof(MultiDataSourceWithMapping).GetProperty("TextView"),
                typeOfTextViewValue, multiDataSourceTreeViewItem);
        }
        #endregion

        #region Взаимодействие с объектами типа DataSourceWithMappingAndClustering
        private void ButtonClusters_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            dynamic dataSource = button.Tag;

            //DataSourceWithMappingAndClustering dataSource = (DataSourceWithMappingAndClustering)button.Tag;
            var dataSourceClone = dataSource.Clone();
            List<string> clasterNames;
            if (dataSource.GetType() == typeof(DataSourceWithMappingAndClustering))
                clasterNames = ((DataSourceWithMappingAndClustering)dataSource).ClusterAndCodes.Keys.ToList();
            else
                clasterNames = ((DataSourceWithMappingAndClusteringAndColors)dataSource).ClusterAndCodes.Keys.ToList();
            dynamic tempList;
            dynamic editListForm;
            if (dataSource.GetType() == typeof(DataSourceWithMappingAndClustering))
            {
                tempList = ((List<string>)clasterNames).Select(x => new NamedObject() { Name = x }).ToList();
                editListForm = new ChangeListWindow(tempList, typeof(NamedObject), dataSource, -1, "Редактирование кластеров");
            }
            else //иначе тип - DataSourceWithMappingAndClusteringAndColors
            {
                tempList = ((List<string>)clasterNames).Select(x => new NamedColoredObject() { Name = x, Color = dataSource.ClusterAndColor[x] }).ToList();
                editListForm = new ChangeListWindow(tempList, typeof(NamedColoredObject), dataSource, -1, "Редактирование кластеров");
            }
            //var tempList = clasterNames.Select(x => new NamedObject() { Name = x }).ToList();
            // var editListForm = new ChangeListWindow(tempList, typeof(NamedObject), -1, "Редактирование");

            editListForm.ShowDialog();
            dataSource.ClusterAndCodes.Clear();
            if (dataSource.GetType() == typeof(DataSourceWithMappingAndClusteringAndColors))
                dataSource.ClusterAndColor.Clear();
            foreach (dynamic element in editListForm.NamedObjects)
            {
                dataSource.ClusterAndCodes.Add(element.Name, new HashSet<int>());
                if (dataSourceClone.ClusterAndCodes.ContainsKey(element.Name))
                {
                    dataSource.ClusterAndCodes[element.Name].UnionWith(dataSourceClone.ClusterAndCodes[element.Name]);
                }
                if (dataSource.GetType() == typeof(DataSourceWithMappingAndClusteringAndColors))
                {
                    dataSource.ClusterAndColor.Add(element.Name, element.Color);
                }
            }
            foreach (var element in dataSource.Mapping)
            {
                if (!dataSource.ClusterAndCodes.ContainsKey(element.Value.Item2))
                    element.Value.Item2 = null;
            }
        }

        private void ClusterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox comboBox = (ComboBox)sender;
            dynamic dataSource = comboBox.Tag;
            dynamic keyValuePair = comboBox.DataContext;
            if (!keyValuePair.GetType().IsGenericType ||
                keyValuePair.GetType().GetGenericTypeDefinition() != typeof(ObservableKeyValuePair<,>))
                return;
            dynamic descriptionAndCluster = keyValuePair.Value;

            int code = keyValuePair.Key;
            string clusterName = descriptionAndCluster.Item2;

            foreach (var pair in dataSource.ClusterAndCodes)
                if (pair.Value.Contains(code))
                    pair.Value.Remove(code);

            if (clusterName != null)
                dataSource.ClusterAndCodes[descriptionAndCluster.Item2].Add(code);
            
        }

        #endregion

        #region Проверки типов
        /// <summary>
        /// Проверка, является ли тип словарем, значениями которого являются наследники NamedObject
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public bool IsDictionaryOfNamedObjects(Type type)
        {
            if ((type.GetGenericTypeDefinition() == typeof(ObservableDictionary<,>) || type.GetGenericTypeDefinition() == typeof(Dictionary<,>)) &&
                     type.GetGenericArguments()[1].IsSubclassOf(typeof(NamedObject)))
                return true;
            else
                return false;
        }
        /// <summary>
        /// Проверка, является ли тип списком или коллекцией, значениями которой являются наследники NamedObject
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public bool IsListOrCollectionOfNamedObjects(Type type)
        {
            if ((type.GetGenericTypeDefinition() == typeof(List<>) || type.GetGenericTypeDefinition() == typeof(ObservableCollection<>)) &&
                     type.GetGenericArguments()[0].IsSubclassOf(typeof(NamedObject)))
                return true;
            else return false;
        }

        /// <summary>
        /// Проверка, является ли тип наследником DataSourceVisual или др. и при этом имеет дополнительное свойство (например: __Unit)
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public bool IsDataSourceHeir(Type type)
        {
            if (type != typeof(DataSourceVisual) && type != typeof(DataSourceWithMapping<string>) && type != typeof(DataSourceWithMappingAndClustering) &&
                type != typeof(DataSourceWithMappingAndColors) && type != typeof(DataSourceWithMappingAndClusteringAndColors) &&
                (type.IsSubclassOf(typeof(DataSourceVisual)) || type.IsSubclassOf(typeof(DataSourceWithMapping<string>)) ||
                    type.IsSubclassOf(typeof(DataSourceWithMappingAndClustering)) || type.IsSubclassOf(typeof(DataSourceWithMappingAndColors)) ||
                    type.IsSubclassOf(typeof(DataSourceWithMappingAndClusteringAndColors))))
            {
                PropertyInfo[] typeProperties = type.GetProperties();
                foreach (PropertyInfo property in typeProperties)
                {
                    string propertyName = property.Name;
                    if (property.PropertyType != typeof(string))
                        continue;

                    if ((propertyName[0] == '_') && (propertyName[1] == '_'))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Проверка, является ли тип непосредственным DataSourceVisual или др.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public bool IsImmediateDataSource(Type type)
        {
            if (type == typeof(DataSourceWithMapping<string>) || type == typeof(DataSourceVisual) || type == typeof(DataSourceWithMappingAndClustering) ||
                type == typeof(DataSourceWithMappingAndColors) || type == typeof(DataSourceWithMappingAndClusteringAndColors))
                return true;
            else
                return false;
        }
        #endregion

        #region Валидация данных
        public void BlockUIElements(bool enable, UIElement exceptUIElement)
        {
            foreach (UIElement element in UIElementsToBlock)
                if (element != exceptUIElement)
                    element.IsEnabled = enable;
            ValidData = enable;
        }
        #endregion

        #region Вспомогательные функции
        private void SaveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            NewOpenSaveFunctions.SaveFile(this.ObjectConfig);
        }

        private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            DataGrid dataGrid = (DataGrid)sender;
            if (e.Key == Key.Up || e.Key == Key.Down) 
            {
                dataGrid.CommitEdit();
            }
            
            if (e.Key == Key.Delete && dataGrid.Tag != null)
            {                
                ComboBox comboBox = (ComboBox)dataGrid.Tag;
                dynamic dataSource = comboBox.SelectedItem;
                if(dataSource.GetType() == typeof(DataSourceWithMappingAndClustering) || dataSource.GetType() == typeof(DataSourceWithMappingAndClusteringAndColors))
                {
                    var selectedItems = dataGrid.SelectedItems;
                    foreach(dynamic keyValuePair in selectedItems)
                    {
                        if (keyValuePair.GetType() == typeof(ObservableKeyValuePair<,>) && keyValuePair.Value.Item2 != null)
                            dataSource.ClusterAndCodes[keyValuePair.Value.Item2].Remove(keyValuePair.Key);
                    }
                }                
            }
        }

        #endregion

    }

    #region Валидация DataGrid

    public class NameCheckRuleInTable : ValidationRule
    {
        public const string NonUniqueName = "Имя не уникально!";
        public const string EmptyName = "Имя не может быть пустым!";

        // Делегированный основным окном метод для блокировки элементо управления
        public Action<bool, UIElement> BlockUIElementsMethod;
        // Таблица с данными
        public DataGrid DataGrid;

        public NameCheckRuleInTable() { }

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            BindingGroup bindingGroup = (BindingGroup)value;
            string name = ((NamedObject)bindingGroup.Items[0]).Name;

            IEnumerable<NamedObject> namedObjects = DataGrid.ItemsSource as IEnumerable<NamedObject>;

            //если объект найден и он не совпадает с тем, который пользователь изменял, значит имя объекта повторяется
            int numOfSameNames = 0;
            foreach (NamedObject namedObject in namedObjects)
                if (namedObject.Name == name)
                    numOfSameNames++;
            bool check = namedObjects.ToList().Select(x => x.Name).ToList().Distinct().Count() == namedObjects.Count();
            BlockUIElementsMethod(check, DataGrid);
            GongSolutions.Wpf.DragDrop.DragDrop.SetIsDragSource(DataGrid, check);
            GongSolutions.Wpf.DragDrop.DragDrop.SetIsDropTarget(DataGrid, check);

            if (numOfSameNames > 1)
                return new ValidationResult(false, NonUniqueName);
            else if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(name))
                return new ValidationResult(false, EmptyName);
            else
                return ValidationResult.ValidResult;
        }
    }

    public class CheckIndexValidationRule : ValidationRule
    {

        // Делегированный основным окном метод для блокировки элементо управления
        public Action<bool, UIElement> BlockUIElementsMethod;
        // Таблица с данными
        public DataGrid DataGrid;

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            BindingGroup bindingGroup = (BindingGroup)value;
            dynamic keyValuePair = bindingGroup.Items[0];
            dynamic dictionary = DataGrid.ItemsSource;

            int valueCount = 0;
            foreach (dynamic keyValue in dictionary)
            {
                if (keyValue.Key == keyValuePair.Key)
                    valueCount++;
            }


            bool check = new List<int>((ICollection<int>)dictionary.Keys).Distinct().Count() == dictionary.Keys.Count;
            BlockUIElementsMethod(check, DataGrid);
            GongSolutions.Wpf.DragDrop.DragDrop.SetIsDragSource(DataGrid, check);
            GongSolutions.Wpf.DragDrop.DragDrop.SetIsDropTarget(DataGrid, check);

            if (valueCount > 1)
            {
                return new ValidationResult(false, "ID  не уникален");
            }
            else if (keyValuePair.Value == null || (keyValuePair.Value is string && keyValuePair.Value == "")
                    || (keyValuePair.Value is Mapping && (keyValuePair.Value.Name == null || keyValuePair.Value.Name == "")))
            {
                return new ValidationResult(false, "Поле с именем не может быть пустым");
            }
            else
            {
                return ValidationResult.ValidResult;
            }
        }
    }
    #endregion

    #region Ширина ComboBox
    public static class ComboBoxWidthFromItemsBehavior
    {
        public static readonly DependencyProperty ComboBoxWidthFromItemsProperty =
            DependencyProperty.RegisterAttached
            (
                "ComboBoxWidthFromItems",
                typeof(bool),
                typeof(ComboBoxWidthFromItemsBehavior),
                new UIPropertyMetadata(false, OnComboBoxWidthFromItemsPropertyChanged)
            );
        public static bool GetComboBoxWidthFromItems(DependencyObject obj)
        {
            return (bool)obj.GetValue(ComboBoxWidthFromItemsProperty);
        }
        public static void SetComboBoxWidthFromItems(DependencyObject obj, bool value)
        {
            obj.SetValue(ComboBoxWidthFromItemsProperty, value);
        }
        private static void OnComboBoxWidthFromItemsPropertyChanged(DependencyObject dpo,
                                                                    DependencyPropertyChangedEventArgs e)
        {
            ComboBox comboBox = dpo as ComboBox;
            if (comboBox != null)
            {
                if ((bool)e.NewValue == true)
                {
                    comboBox.SelectionChanged += OnComboBoxLoaded;
                }
                else
                {
                    comboBox.SelectionChanged -= OnComboBoxLoaded;
                }
            }
        }
        private static void OnComboBoxLoaded(object sender, RoutedEventArgs e)
        {
            ComboBox comboBox = sender as ComboBox;
            Action action = () => { comboBox.SetWidthFromItems(); };
            comboBox.Dispatcher.BeginInvoke(action, DispatcherPriority.ContextIdle);
        }
    }

    public static class ComboBoxExtensionMethods
    {
        public static void SetWidthFromItems(this ComboBox comboBox)
        {
            double comboBoxWidth = 19;// comboBox.DesiredSize.Width;            
            // Create the peer and provider to expand the comboBox in code behind. 
            ComboBoxAutomationPeer peer = new ComboBoxAutomationPeer(comboBox);
            IExpandCollapseProvider provider = (IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse);
            EventHandler eventHandler = null;
            eventHandler = new EventHandler(delegate
            {
                if (comboBox.IsDropDownOpen &&
                    comboBox.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
                {
                    double width = 0;
                    foreach (var item in comboBox.Items)
                    {
                        ComboBoxItem comboBoxItem = comboBox.ItemContainerGenerator.ContainerFromItem(item) as ComboBoxItem;
                        comboBoxItem.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        if (comboBoxItem.DesiredSize.Width > width)
                        {
                            width = comboBoxItem.DesiredSize.Width;
                        }
                    }
                    comboBox.Width = comboBoxWidth + width;
                    // Remove the event handler. 
                    comboBox.ItemContainerGenerator.StatusChanged -= eventHandler;
                    comboBox.DropDownOpened -= eventHandler;
                    bool enableComboBoxDelegate = comboBox.IsEnabled;
                    comboBox.IsEnabled = true;
                    provider.Collapse();
                    comboBox.IsEnabled = enableComboBoxDelegate;
                }
            });
            comboBox.ItemContainerGenerator.StatusChanged += eventHandler;
            comboBox.DropDownOpened += eventHandler;
            bool enableComboBox = comboBox.IsEnabled;
            comboBox.IsEnabled = true;
            // Expand the comboBox to generate all its ComboBoxItem's. 
            provider.Expand();
            comboBox.IsEnabled = enableComboBox;
        }
    }
    #endregion
}
