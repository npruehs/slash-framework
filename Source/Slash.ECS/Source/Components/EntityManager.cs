﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="EntityManager.cs" company="Slash Games">
//   Copyright (c) Slash Games. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace Slash.ECS.Components
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;

    using Slash.Collections.AttributeTables;
    using Slash.Collections.ObjectModel;
    using Slash.ECS.Events;
    using Slash.ECS.Inspector.Attributes;
    using Slash.ECS.Inspector.Data;
    using Slash.ECS.Inspector.Utils;
    using Slash.ECS.Logging;

    /// <summary>
    ///   Delegate for registering for when a component of a specific type was added.
    /// </summary>
    /// <param name="entityId">Id of entity the component was added to.</param>
    /// <param name="component">Component which was added.</param>
    /// <typeparam name="T">Type of component to register for.</typeparam>
    public delegate void ComponentAddedDelegate<in T>(int entityId, T component);

    /// <summary>
    ///   Delegate for registering for when a component of a specific type was removed.
    /// </summary>
    /// <param name="entityId">Id of entity the component was removed from.</param>
    /// <param name="component">Component which was removed.</param>
    /// <typeparam name="T">Type of component to register for.</typeparam>
    public delegate void ComponentRemovedDelegate<in T>(int entityId, T component);

    /// <summary>
    ///   Delegate for EntityManager.EntityInitialized event.
    /// </summary>
    /// <param name="entityId">Id of initialized entity.</param>
    public delegate void EntityInitializedDelegate(int entityId);

    /// <summary>
    ///   Delegate for EntityInitialized.EntityRemoved event.
    /// </summary>
    /// <param name="entityId">Id of removed entity.</param>
    public delegate void EntityRemovedDelegate(int entityId);

    /// <summary>
    ///   Creates and removes game entities. Holds references to all component
    ///   managers, delegating all calls for adding or removing components.
    /// </summary>
    public class EntityManager
    {
        #region Fields

        /// <summary>
        ///   Managers that are mapping entity ids to specific components.
        /// </summary>
        private readonly Dictionary<Type, ComponentManager> componentManagers;

        /// <summary>
        ///   All active entity ids.
        /// </summary>
        private readonly HashSet<int> entities;

        /// <summary>
        ///   Event manager to send entity events to.
        /// </summary>
        private readonly EventManager eventManager;

        /// <summary>
        ///   Inactive entities and their components.
        /// </summary>
        private readonly Dictionary<int, List<IEntityComponent>> inactiveEntities;

        /// <summary>
        ///   Inspector types of entity components.
        /// </summary>
        private readonly InspectorTypeTable inspectorTypes;

        /// <summary>
        ///   Logger.
        /// </summary>
        private readonly GameLogger log;

        /// <summary>
        ///   Ids of all entities that have been removed in this tick.
        /// </summary>
        private readonly HashSet<int> removedEntities;

        /// <summary>
        ///   Id that will be assigned to the next entity created.
        /// </summary>
        private int nextEntityId;

        #endregion

        #region Constructors and Destructors

        /// <summary>
        ///   Constructs a new entity manager without any initial entities.
        /// </summary>
        /// <param name="eventManager">Event manager to send entity events to.</param>
        /// <param name="log">Logger.</param>
        public EntityManager(EventManager eventManager, GameLogger log = null)
        {
            if (log == null)
            {
                log = new GameLogger();
            }

            this.eventManager = eventManager;
            this.log = log;
            this.nextEntityId = 1;
            this.entities = new HashSet<int>();
            this.removedEntities = new HashSet<int>();
            this.inactiveEntities = new Dictionary<int, List<IEntityComponent>>();
            this.componentManagers = new Dictionary<Type, ComponentManager>();
            this.inspectorTypes = InspectorTypeTable.FindInspectorTypes(typeof(IEntityComponent));
        }

        #endregion

        #region Events

        /// <summary>
        ///   Entity has been created and all components have been added and initialized.
        /// </summary>
        public event EntityInitializedDelegate EntityInitialized;

        /// <summary>
        ///   Entity and all of its components will be removed at the end of this tick.
        /// </summary>
        public event EntityRemovedDelegate EntityRemoved;

        #endregion

        #region Properties

        /// <summary>
        ///   Read-only collection of all entities.
        /// </summary>
        public IEnumerable<int> Entities
        {
            get
            {
                return new ReadOnlyCollection<int>(this.entities);
            }
        }

        /// <summary>
        ///   Total number of entities managed by this EntityManager instance.
        /// </summary>
        public int EntityCount
        {
            get
            {
                return this.entities.Count;
            }
        }

        #endregion

        #region Public Methods and Operators

        /// <summary>
        ///   Re-activates the entity with the specified id, if it is inactive.
        /// </summary>
        /// <param name="entityId">Id of the entity to activate.</param>
        public void ActivateEntity(int entityId)
        {
            // Check if entity is inactive.
            List<IEntityComponent> components;

            if (!this.inactiveEntities.TryGetValue(entityId, out components))
            {
                return;
            }

            // Activate entity.
            this.CreateEntity(entityId);

            // Add components.
            foreach (IEntityComponent component in components)
            {
                this.AddComponent(entityId, component);
            }

            // Raise event.
            this.OnEntityInitialized(entityId);

            // Remove from list of inactive entities.
            this.inactiveEntities.Remove(entityId);
        }

        /// <summary>
        ///   Attaches the passed component to the entity with the specified id.
        /// </summary>
        /// <param name="entityId"> Id of the entity to attach the component to. </param>
        /// <param name="component"> Component to attach. </param>
        /// <exception cref="ArgumentOutOfRangeException">Entity id is negative.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Entity id has not yet been assigned.</exception>
        /// <exception cref="ArgumentException">Entity with the specified id has already been removed.</exception>
        /// <exception cref="ArgumentNullException">Passed component is null.</exception>
        /// <exception cref="InvalidOperationException">There is already a component of the same type attached.</exception>
        public void AddComponent(int entityId, IEntityComponent component)
        {
            this.AddComponent(entityId, component, true);
        }

        /// <summary>
        ///   Attaches a new component of the passed type to the entity with the specified id.
        /// </summary>
        /// <typeparam name="T">Type of the component to add.</typeparam>
        /// <param name="entityId">Id of the entity to attach the component to.</param>
        /// <returns>Attached component.</returns>
        public T AddComponent<T>(int entityId) where T : IEntityComponent, new()
        {
            T component = new T();
            this.AddComponent(entityId, component, true);
            return component;
        }

        /// <summary>
        ///   Attaches the passed component to the entity with the specified id.
        /// </summary>
        /// <param name="entityId"> Id of the entity to attach the component to. </param>
        /// <param name="component"> Component to attach. </param>
        /// <param name="sendEvent">Indicates if an event should be send about the component adding.</param>
        /// <exception cref="ArgumentOutOfRangeException">Entity id is negative.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Entity id has not yet been assigned.</exception>
        /// <exception cref="ArgumentException">Entity with the specified id has already been removed.</exception>
        /// <exception cref="ArgumentNullException">Passed component is null.</exception>
        /// <exception cref="InvalidOperationException">There is already a component of the same type attached.</exception>
        public void AddComponent(int entityId, IEntityComponent component, bool sendEvent)
        {
            this.CheckEntityId(entityId);

            Type componentType = component.GetType();

            ComponentManager componentManager = this.GetComponentManager(componentType, true);
            componentManager.AddComponent(entityId, component);

            if (sendEvent)
            {
                this.eventManager.QueueEvent(
                    FrameworkEvent.ComponentAdded,
                    new EntityComponentData(entityId, component));
            }
        }

        /// <summary>
        ///   Adds a component with the specified type to entity with the
        ///   specified id and initializes it with the values taken from
        ///   the passed attribute table.
        /// </summary>
        /// <param name="componentType">Type of the component to add.</param>
        /// <param name="entityId">Id of the entity to add the component to.</param>
        /// <param name="attributeTable">Attribute table to initialize the component with.</param>
        public void AddComponent(Type componentType, int entityId, IAttributeTable attributeTable)
        {
            // Create component.
            IEntityComponent component = (IEntityComponent)Activator.CreateInstance(componentType);

            // Init component.
            this.InitComponent(component, attributeTable);

            // Initialize component with the attribute table data.
            component.InitComponent(attributeTable);

            // Add component. 
            this.AddComponent(entityId, component);
        }

        /// <summary>
        ///   Removes all entities that have been issued for removal during the
        ///   current tick, detaching all components.
        /// </summary>
        public void CleanUpEntities()
        {
            // Store entities to remove as more entities might marked as removed
            // inside the loop due to events.
            var entitiesToRemove = new List<int>(this.removedEntities);
            this.removedEntities.Clear();

            foreach (int id in entitiesToRemove)
            {
                // Remove components.
                foreach (ComponentManager manager in this.componentManagers.Values)
                {
                    manager.RemoveComponent(id);
                }

                this.entities.Remove(id);
            }
        }

        /// <summary>
        ///   Returns an iterator over all components of the specified type.
        /// </summary>
        /// <param name="type"> Type of the components to get. </param>
        /// <returns> Components of the specified type. </returns>
        /// <exception cref="ArgumentNullException">Specified type is null.</exception>
        public IEnumerable ComponentsOfType(Type type)
        {
            ComponentManager componentManager;

            if (type == null)
            {
                throw new ArgumentNullException("type");
            }

            if (!this.componentManagers.TryGetValue(type, out componentManager))
            {
                yield break;
            }

            foreach (IEntityComponent component in componentManager.Components())
            {
                yield return component;
            }
        }

        /// <summary>
        ///   Creates a new entity.
        /// </summary>
        /// <returns> Unique id of the new entity. </returns>
        public int CreateEntity()
        {
            int id = this.nextEntityId++;
            return this.CreateEntity(id);
        }

        /// <summary>
        ///   Creates a new entity with the specified id.
        /// </summary>
        /// <param name="id">Id of the entity to create.</param>
        /// <returns>Unique id of the new entity.</returns>
        public int CreateEntity(int id)
        {
            if (!this.entities.Add(id))
            {
                throw new ArgumentException(
                    "An entity with id " + id + " couldn't be created, id already exists.",
                    "id");
            }

            // Adjust next entity id.
            this.nextEntityId = Math.Max(this.nextEntityId, id + 1);

            this.eventManager.QueueEvent(FrameworkEvent.EntityCreated, id);
            return id;
        }

        /// <summary>
        ///   De-activates the entity with the specified id. Inactive entities
        ///   are considered as removed, until they are re-activated again.
        /// </summary>
        /// <param name="entityId">Id of the entity to de-activate.</param>
        public void DeactivateEntity(int entityId)
        {
            // Check if entity is active.
            if (this.inactiveEntities.ContainsKey(entityId))
            {
                return;
            }

            // Store entity components and their values.
            List<IEntityComponent> components = new List<IEntityComponent>();

            foreach (ComponentManager manager in this.componentManagers.Values)
            {
                IEntityComponent component;
                if (!manager.RemoveComponent(entityId, out component))
                {
                    continue;
                }

                components.Add(component);

                this.eventManager.QueueEvent(
                    FrameworkEvent.ComponentRemoved,
                    new EntityComponentData(entityId, component));
            }

            // Remove entity.
            this.RemoveEntity(entityId);

            // Add to list of inactive entities.
            this.inactiveEntities.Add(entityId, components);
        }

        /// <summary>
        ///   Returns an iterator over all entities having components of the specified type attached.
        /// </summary>
        /// <param name="type"> Type of the components to get the entities of. </param>
        /// <returns> Entities having components of the specified type attached. </returns>
        /// <exception cref="ArgumentNullException">Specified type is null.</exception>
        public IEnumerable<int> EntitiesWithComponent(Type type)
        {
            ComponentManager componentManager;

            if (type == null)
            {
                throw new ArgumentNullException("type");
            }

            if (!this.componentManagers.TryGetValue(type, out componentManager))
            {
                yield break;
            }

            foreach (int entityId in componentManager.Entities())
            {
                yield return entityId;
            }
        }

        /// <summary>
        ///   Checks whether the entity with the passed id has been removed or
        ///   not.
        /// </summary>
        /// <param name="entityId"> Id of the entity to check. </param>
        /// <returns>
        ///   <c>false</c> , if the entity has been removed, and <c>true</c> otherwise.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">Entity id is negative.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Entity id has not yet been assigned.</exception>
        public bool EntityIsAlive(int entityId)
        {
            if (entityId < 0)
            {
                throw new ArgumentOutOfRangeException("entityId", "Entity ids are always non-negative.");
            }

            if (entityId >= this.nextEntityId)
            {
                throw new ArgumentOutOfRangeException(
                    "entityId",
                    "Entity id " + entityId + " has not yet been assigned.");
            }

            return this.entities.Contains(entityId);
        }

        /// <summary>
        ///   Checks if the entity with the specified id will be removed this
        ///   frame.
        /// </summary>
        /// <param name="entityId">Id of the entity to check.</param>
        /// <returns>
        ///   <c>true</c>, if the entity with the specified id is about to be removed, and
        ///   <c>false</c>, otherwise.
        /// </returns>
        public bool EntityIsBeingRemoved(int entityId)
        {
            return this.removedEntities.Contains(entityId);
        }

        /// <summary>
        ///   Checks whether the entity with the specified id is inactive.
        /// </summary>
        /// <param name="id">Id of the entity to check.</param>
        /// <returns>
        ///   <c>true</c>, if the entity is inactive, and
        ///   <c>false</c> otherwise.
        /// </returns>
        public bool EntityIsInactive(int id)
        {
            return this.inactiveEntities.ContainsKey(id);
        }

        /// <summary>
        ///   Gets a component of the passed type attached to the entity with the specified id.
        /// </summary>
        /// <param name="entityId"> Id of the entity to get the component of. </param>
        /// <param name="componentType"> Type of the component to get. </param>
        /// <param name="considerInherited">
        ///   Indicates if a component that was inherited by the specified type should be returned if
        ///   found.
        /// </param>
        /// <returns> The component, if there is one of the specified type attached to the entity, and null otherwise. </returns>
        /// <exception cref="ArgumentOutOfRangeException">Entity id is negative.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Entity id has not yet been assigned.</exception>
        /// <exception cref="ArgumentException">Entity with the specified id has already been removed.</exception>
        /// <exception cref="ArgumentNullException">Passed component type is null.</exception>
        public IEntityComponent GetComponent(int entityId, Type componentType, bool considerInherited = false)
        {
            this.CheckEntityId(entityId);

            if (componentType == null)
            {
                throw new ArgumentNullException("componentType");
            }

            // Check all component managers if inherited types should be considered.
            if (considerInherited)
            {
                foreach (KeyValuePair<Type, ComponentManager> componentManagerPair in this.componentManagers)
                {
                    if (componentType.IsAssignableFrom(componentManagerPair.Key))
                    {
                        IEntityComponent component = componentManagerPair.Value.GetComponent(entityId);
                        if (component != null)
                        {
                            return component;
                        }
                    }
                }
                return null;
            }

            ComponentManager componentManager;
            return this.componentManagers.TryGetValue(componentType, out componentManager)
                ? componentManager.GetComponent(entityId)
                : null;
        }

        /// <summary>
        ///   Gets a component of the passed type attached to the entity with the specified id.
        /// </summary>
        /// <param name="entityId"> Id of the entity to get the component of. </param>
        /// <typeparam name="T"> Type of the component to get. </typeparam>
        /// <returns> The component, if there is one of the specified type attached to the entity, and null otherwise. </returns>
        /// <exception cref="ArgumentOutOfRangeException">Entity id is negative.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Entity id has not yet been assigned.</exception>
        /// <exception cref="ArgumentException">Entity with the specified id has already been removed.</exception>
        /// <exception cref="ArgumentNullException">Passed component type is null.</exception>
        /// <exception cref="ArgumentException">A component of the passed type has never been added before.</exception>
        public T GetComponent<T>(int entityId) where T : IEntityComponent
        {
            return (T)this.GetComponent(entityId, typeof(T));
        }

        /// <summary>
        ///   Returns all components of the entity with the specified id.
        /// </summary>
        /// <param name="entityId">Id of entity to get components for.</param>
        /// <returns>All components of the entity with the specified id.</returns>
        public IEnumerable<IEntityComponent> GetComponents(int entityId)
        {
            return
                this.componentManagers.Values.Select(componentManager => componentManager.GetComponent(entityId))
                    .Where(entityComponent => entityComponent != null);
        }

        /// <summary>
        ///   Retrieves an array containing the ids of all living entities in
        ///   O(n).
        /// </summary>
        /// <returns> Array containing the ids of all entities that haven't been removed yet. </returns>
        public int[] GetEntities()
        {
            if (this.entities.Count == 0)
            {
                return null;
            }

            int[] entityArray = new int[this.entities.Count];
            this.entities.CopyTo(entityArray);
            return entityArray;
        }

        /// <summary>
        ///   Returns the entity ids of all entities which fulfill the specified predicate.
        /// </summary>
        /// <param name="predicate"> Predicate to fulfill. </param>
        /// <returns> Collection of ids of all entities which fulfill the specified predicate. </returns>
        public IEnumerable<int> GetEntities(Func<int, bool> predicate)
        {
            return this.entities.Count == 0 ? null : this.entities.Where(predicate);
        }

        /// <summary>
        ///   Convenience method for retrieving a component from two possible entities.
        /// </summary>
        /// <typeparam name="TComponent">Type of the component to get.</typeparam>
        /// <param name="data">Data for the event that affected two entities.</param>
        /// <param name="entityId">Id of the entity having the component attached.</param>
        /// <param name="component">Component.</param>
        /// <returns>
        ///   True if one of the entities has a <typeparamref name="TComponent" />
        ///   attached; otherwise, false.
        /// </returns>
        public bool GetEntityComponent<TComponent>(Entity2Data data, out int entityId, out TComponent component)
            where TComponent : class, IEntityComponent
        {
            int entityIdA = data.First;
            int entityIdB = data.Second;

            TComponent componentA = this.GetComponent<TComponent>(entityIdA);
            if (componentA != null)
            {
                entityId = entityIdA;
                component = componentA;
                return true;
            }

            TComponent componentB = this.GetComponent<TComponent>(entityIdB);
            if (componentB != null)
            {
                entityId = entityIdB;
                component = componentB;
                return true;
            }

            entityId = 0;
            component = null;
            return false;
        }

        /// <summary>
        ///   Convenience method for retrieving components from two entities
        ///   in case the order of the entities is unknown.
        /// </summary>
        /// <typeparam name="TComponentTypeA">Type of the first component to get.</typeparam>
        /// <typeparam name="TComponentTypeB">Type of the second component to get.</typeparam>
        /// <param name="data">Data for the event that affected two entities.</param>
        /// <param name="entityIdA">Id of the entity having the first component attached.</param>
        /// <param name="entityIdB">Id of the entity having the second component attached.</param>
        /// <param name="componentA">First component.</param>
        /// <param name="componentB">Second component.</param>
        /// <returns>
        ///   <c>true</c>, if one of the entities has a <typeparamref name="TComponentTypeA" />
        ///   and the other one a <typeparamref name="TComponentTypeB" /> attached,
        ///   and <c>false</c> otherwise.
        /// </returns>
        public bool GetEntityComponents<TComponentTypeA, TComponentTypeB>(
            Entity2Data data,
            out int entityIdA,
            out int entityIdB,
            out TComponentTypeA componentA,
            out TComponentTypeB componentB) where TComponentTypeA : class, IEntityComponent
            where TComponentTypeB : class, IEntityComponent
        {
            entityIdA = data.First;
            entityIdB = data.Second;

            componentA = this.GetComponent<TComponentTypeA>(entityIdA);
            componentB = this.GetComponent<TComponentTypeB>(entityIdB);

            if (componentA == null || componentB == null)
            {
                // Check other way round.
                entityIdA = data.Second;
                entityIdB = data.First;

                componentA = this.GetComponent<TComponentTypeA>(entityIdA);
                componentB = this.GetComponent<TComponentTypeB>(entityIdB);

                return componentA != null && componentB != null;
            }

            return true;
        }

        /// <summary>
        ///   Initializes the specified entity, adding the specified components.
        /// </summary>
        /// <param name="entityId">Id of the entity to initialize.</param>
        /// <param name="components">Initialized components to add to the entity.</param>
        public void InitEntity(int entityId, IEnumerable<IEntityComponent> components)
        {
            // Add components.
            foreach (IEntityComponent component in components)
            {
                this.AddComponent(entityId, component);
            }

            // Raise event.
            this.OnEntityInitialized(entityId);
        }

        /// <summary>
        ///   Called when the entity with the specified id was initialized.
        ///   TODO(co): This shouldn't be public.
        /// </summary>
        /// <param name="entityId">Id of initialized entity.</param>
        public void OnEntityInitialized(int entityId)
        {
            var handler = this.EntityInitialized;
            if (handler != null)
            {
                handler(entityId);
            }

            this.eventManager.QueueEvent(FrameworkEvent.EntityInitialized, entityId);
        }

        /// <summary>
        ///   Registers listeners to track adding/removing of components of type T.
        /// </summary>
        /// <typeparam name="T">Type of component to track.</typeparam>
        /// <param name="onComponentAdded">Callback when a new component of the type was added.</param>
        /// <param name="onComponentRemoved">Callback when a component of the type was removed.</param>
        public void RegisterComponentListeners<T>(
            ComponentAddedDelegate<T> onComponentAdded,
            ComponentRemovedDelegate<T> onComponentRemoved)
        {
            Type componentType = typeof(T);
            this.RegisterComponentListeners(
                componentType,
                (entityId, component) => onComponentAdded(entityId, (T)component),
                (entityId, component) => onComponentRemoved(entityId, (T)component));
        }

        /// <summary>
        ///   Registers listeners to track adding/removing of components of specified component type.
        /// </summary>
        /// <param name="componentType">Type of component to track.</param>
        /// <param name="onComponentAdded">Callback when a new component of the type was added.</param>
        /// <param name="onComponentRemoved">Callback when a component of the type was removed.</param>
        public void RegisterComponentListeners(
            Type componentType,
            ComponentAddedDelegate<object> onComponentAdded,
            ComponentRemovedDelegate<object> onComponentRemoved)
        {
            ComponentManager componentManager = this.GetComponentManager(componentType, true);
            componentManager.ComponentAdded += onComponentAdded;
            componentManager.ComponentRemoved += onComponentRemoved;
        }

        /// <summary>
        ///   Removes a component of the passed type from the entity with the specified id.
        /// </summary>
        /// <param name="entityId"> Id of the entity to remove the component from. </param>
        /// <param name="componentType"> Type of the component to remove. </param>
        /// <returns> Whether a component has been removed, or not. </returns>
        /// <exception cref="ArgumentOutOfRangeException">Entity id is negative.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Entity id has not yet been assigned.</exception>
        /// <exception cref="ArgumentException">Entity with the specified id has already been removed.</exception>
        /// <exception cref="ArgumentNullException">Passed component type is null.</exception>
        /// <exception cref="ArgumentException">A component of the passed type has never been added before.</exception>
        public bool RemoveComponent(int entityId, Type componentType)
        {
            this.CheckEntityId(entityId);

            if (componentType == null)
            {
                throw new ArgumentNullException("componentType");
            }

            ComponentManager componentManager;

            if (!this.componentManagers.TryGetValue(componentType, out componentManager))
            {
                throw new ArgumentException(
                    "A component of type " + componentType + " has never been added before.",
                    "componentType");
            }

            IEntityComponent component;
            bool removed = componentManager.RemoveComponent(entityId, out component);
            if (removed)
            {
                // Deinitialize component.
                this.DeinitComponent(component);

                this.eventManager.QueueEvent(
                    FrameworkEvent.ComponentRemoved,
                    new EntityComponentData(entityId, component));
            }

            return removed;
        }

        /// <summary>
        ///   Removes all entities.
        /// </summary>
        public void RemoveEntities()
        {
            IEnumerable<int> aliveEntities = this.entities.Except(this.removedEntities);
            foreach (int entityId in aliveEntities)
            {
                this.OnEntityRemoved(entityId);

                // Remove components.
                foreach (ComponentManager manager in this.componentManagers.Values)
                {
                    IEntityComponent component;
                    if (manager.RemoveComponent(entityId, out component))
                    {
                        this.eventManager.QueueEvent(
                            FrameworkEvent.ComponentRemoved,
                            new EntityComponentData(entityId, component));
                    }
                }

                this.removedEntities.Add(entityId);
            }
        }

        /// <summary>
        ///   <para>
        ///     Issues the entity with the specified id for removal at the end of
        ///     the current tick.
        ///   </para>
        ///   <para>
        ///     If the entity is inactive, it is removed immediately and no
        ///     further event is raised.
        ///   </para>
        /// </summary>
        /// <param name="entityId"> Id of the entity to remove. </param>
        /// <exception cref="ArgumentOutOfRangeException">Entity id is negative.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Entity id has not yet been assigned.</exception>
        /// <exception cref="ArgumentException">Entity with the specified id has already been removed.</exception>
        public void RemoveEntity(int entityId)
        {
            if (this.EntityIsInactive(entityId))
            {
                this.inactiveEntities.Remove(entityId);
                return;
            }

            this.CheckEntityId(entityId);

            if (this.EntityIsBeingRemoved(entityId))
            {
                return;
            }

            // Remove components.
            foreach (ComponentManager manager in this.componentManagers.Values)
            {
                IEntityComponent component = manager.GetComponent(entityId);
                if (component == null)
                {
                    continue;
                }

                this.eventManager.QueueEvent(
                    FrameworkEvent.ComponentRemoved,
                    new EntityComponentData(entityId, component));
            }

            this.OnEntityRemoved(entityId);

            this.removedEntities.Add(entityId);
        }

        /// <summary>
        ///   Tries to get a component of the passed type attached to the entity with the specified id.
        /// </summary>
        /// <param name="entityId">Id of the entity to get the component of.</param>
        /// <param name="componentType">Type of the component to get.</param>
        /// <param name="entityComponent">Retrieved entity component, or null, if no component could be found.</param>
        /// <returns>
        ///   <c>true</c>, if a component could be found, and <c>false</c> otherwise.
        /// </returns>
        public bool TryGetComponent(int entityId, Type componentType, out IEntityComponent entityComponent)
        {
            entityComponent = this.GetComponent(entityId, componentType);
            return entityComponent != null;
        }

        /// <summary>
        ///   Tries to get a component of the passed type attached to the entity with the specified id.
        /// </summary>
        /// <typeparam name="T">Type of the component to get.</typeparam>
        /// <param name="entityId">Id of the entity to get the component of.</param>
        /// <param name="entityComponent">Retrieved entity component, or null, if no component could be found.</param>
        /// <returns>
        ///   <c>true</c>, if a component could be found, and <c>false</c> otherwise.
        /// </returns>
        public bool TryGetComponent<T>(int entityId, out T entityComponent) where T : IEntityComponent
        {
            entityComponent = this.GetComponent<T>(entityId);
            return !Equals(entityComponent, default(T));
        }

        #endregion

        #region Methods

        /// <summary>
        ///   Checks whether the passed entity is valid, throwing an exception if not.
        /// </summary>
        /// <param name="id"> Entity id to check. </param>
        /// <exception cref="ArgumentOutOfRangeException">Entity id is negative.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Entity id has not yet been assigned.</exception>
        /// <exception cref="ArgumentException">Entity with the specified id has already been removed.</exception>
        private void CheckEntityId(int id)
        {
            if (!this.EntityIsAlive(id))
            {
                throw new ArgumentException("id", "The entity with id " + id + " has already been removed.");
            }
        }

        /// <summary>
        ///   Deinitializes the specified component.
        /// </summary>
        /// <param name="component">Component to deinitialize.</param>
        private void DeinitComponent(IEntityComponent component)
        {
            InspectorType inspectorType;
            if (!this.inspectorTypes.TryGetInspectorType(component.GetType(), out inspectorType))
            {
                this.log.Warning(
                    "Entity component '" + component.GetType() + "' not flagged as inspector type, can't deinitialize.");
                return;
            }

            InspectorUtils.Deinit(this, inspectorType, component);
        }

        private ComponentManager GetComponentManager(Type componentType, bool createIfNecessary)
        {
            ComponentManager componentManager;
            if (!this.componentManagers.TryGetValue(componentType, out componentManager) && createIfNecessary)
            {
                componentManager = new ComponentManager();
                this.componentManagers.Add(componentType, componentManager);
            }
            return componentManager;
        }

        /// <summary>
        ///   Initializes the specified component with the specified attribute table.
        /// </summary>
        /// <param name="component">Component to initialize.</param>
        /// <param name="attributeTable">Attribute table which contains the data of the component.</param>
        private void InitComponent(IEntityComponent component, IAttributeTable attributeTable)
        {
            InspectorType inspectorType;
            if (!this.inspectorTypes.TryGetInspectorType(component.GetType(), out inspectorType))
            {
                this.log.Warning(
                    "Entity component '" + component.GetType()
                    + "' not flagged as inspector type, can't initialize via reflection.");
                return;
            }

            var inspectorComponent = inspectorType.Attribute as InspectorComponentAttribute;
            if (inspectorComponent != null && inspectorComponent.InitExplicitly)
            {
                return;
            }

            InspectorUtils.InitFromAttributeTable(this, inspectorType, component, attributeTable);
        }

        private void OnEntityRemoved(int entityId)
        {
            var handler = this.EntityRemoved;
            if (handler != null)
            {
                handler(entityId);
            }

            this.eventManager.QueueEvent(FrameworkEvent.EntityRemoved, entityId);
        }

        #endregion
    }
}