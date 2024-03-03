using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace Engineless {

    public enum Event {
        Startup,
        Update,
    }

    public abstract class Component { }
    // These empty classes are used to what resources to fetch
    public class Query<T> { public List<KeyValuePair<int, T>> hits; }
    
    // State class for the ECS
    public class Engine : IECS {
        private List<Delegate> updateSystems = new();
        private List<Delegate> startupSystems = new();
        private Dictionary<Type, Dictionary<int, Component>> allComponents = new();
        private int entityIndex = 0;
        private bool running = true;

        public Engine AddSystem(Event e, Delegate system) {
            if (e == Event.Startup) {
                startupSystems.Add(system);
            }
            if (e == Event.Update) {
                updateSystems.Add(system);
            }
            return this;
        }

        // Game loop
        public void Start() {
            Console.WriteLine("Starting Engine!");
            foreach (Delegate system in startupSystems) {
                HandleSystem(system);
            }
            // Start game loop
            while (running) {
                System.Threading.Thread.Sleep(5);
                foreach (Delegate system in updateSystems) {
                    HandleSystem(system);
                }
            }
        }

        private void HandleSystem(Delegate system) {
            MethodInfo methodInfo = system.Method;
            ParameterInfo[] parameters = methodInfo.GetParameters();
            List<Object> systemArguments = new();
            foreach (ParameterInfo parameter in parameters) {
                Type parameterType = parameter.ParameterType;
                if (parameterType == typeof(IECS)) {
                    systemArguments.Add(this);
                } else if (parameterType.IsGenericType && parameterType.GetGenericTypeDefinition() == typeof(Query<>)) {
                    // Two cases:
                    // * One generic argument, provide that list directly
                    // * Tuple of arguments
                    Type typeArgument = parameterType.GetGenericArguments()[0];

                    // Create the query object
                    var queryType = typeof(Query<>);
                    var queryTypeFulfilled = queryType.MakeGenericType(typeArgument);
                    var queryInstance = Activator.CreateInstance(queryTypeFulfilled);
                    var fieldInfo = queryTypeFulfilled.GetField("hits", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    Type[] queryTypes = typeArgument.GetGenericArguments();

                    if (queryTypes == null || queryTypes.Length == 0) {
                        // One argument
                        fieldInfo.SetValue(queryInstance,
                                allComponents.ContainsKey(typeArgument) ?
                                    allComponents[typeArgument] :
                                    RCreateEmptyList(typeArgument));
                        systemArguments.Add(queryInstance);
                    } else {
                        // Two or more arguments
                        // Only interested in the columns with the tuple types
                        // All must be present
                        // 'Type' needed to construct the tuples
                        List<(Type, Dictionary<int, Component>)> cs = new();
                        bool componentColumnsExist = true;
                        foreach (Type t in queryTypes) {
                            if (allComponents.ContainsKey(t)) {
                                cs.Add((t, allComponents[t]));
                            } else {
                                componentColumnsExist = false;
                            }
                        }
                        if (!componentColumnsExist) { 
                            fieldInfo.SetValue(queryInstance, RCreateEmptyList(
                                        typeof(KeyValuePair<,>).MakeGenericType(typeof(int), typeArgument)
                                        ));
                            systemArguments.Add(queryInstance);
                            continue;
                        }

                        var smallest = cs.Aggregate(cs[0],
                            (shortest, next) =>
                                next.Item2.Count < shortest.Item2.Count ? next : shortest);

                        // Type: (c1, c2, c3)      (c1-3 are known types)
                        Type tupleListGenericType = Type.GetType("System.ValueTuple`" + cs.Count);
                        Type[] tupleListTypeArgs = cs.Select(p => p.Item1).ToArray();
                        Type tupleType = tupleListGenericType.MakeGenericType(tupleListTypeArgs);

                        // Type: KveyValuePair<int, (c1, c2, c3)>
                        Type kvPairType = typeof(KeyValuePair<,>).MakeGenericType(typeof(int), tupleType);

                        // Type: List<KveyValuePair<int, (c1, c2, c3)>>
                        Type listType = typeof(List<>).MakeGenericType(kvPairType);

                        // Instance: List<KveyValuePair<int, (c1, c2, c3)>>
                        Object listInstance = Activator.CreateInstance(listType);

                        // MethodInfo: List<KveyValuePair<int, (c1, c2, c3)>>.Add
                        MethodInfo listAdd = listType.GetMethod("Add", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                        // Use shortest to check for tuple matches
                        cs.Remove(smallest);
                        var restOfColumns = cs;

                        // List of tuples of the query
                        foreach (var c in smallest.Item2) {
                            List<(Type, Object)> tupleComponents = new() { (smallest.Item1, c.Value) };
                            bool tupleComplete = true;
                            foreach (var column in restOfColumns) {
                                if (column.Item2.ContainsKey(c.Key)) {
                                    tupleComponents.Add((column.Item1, column.Item2[c.Key]));
                                } else {
                                    tupleComplete = false;
                                    break;
                                }
                            }
                            if (!tupleComplete) {
                                continue;
                            } 
                            // Construct the tuple to add to queryResult
                            var kvInstance = Activator.CreateInstance(kvPairType, c.Key, RGetTuple(tupleComponents));
                            listAdd.Invoke(listInstance, new Object[]{kvInstance});
                        }
                        fieldInfo.SetValue(queryInstance, listInstance);
                        systemArguments.Add(queryInstance);
                    }

                } else {
                    throw new InvalidOperationException("Systems cannot aquire anything other than IECS and Query");
                }
                
            }
            system.DynamicInvoke(systemArguments.ToArray());
        }

        public void AddEntity(List<Component> components) {
            foreach (Component c in components) {
                if (!allComponents.ContainsKey(c.GetType())
                        || allComponents[c.GetType()] == null) {
                    // First component of this type
                    allComponents[c.GetType()] = new();
                }
                allComponents[c.GetType()].Add(entityIndex, c);
            }
            entityIndex += 1;
        }

        // Reflection helper methods ('R' is for reflection)
        private Object RGetTuple(List<(Type, Object)> input) {
            Type genericType = Type.GetType("System.ValueTuple`" + input.Count);
            Type[] typeArgs = input.Select(p => p.Item1).ToArray();
            Object[] valueArgs = input.Select(p => p.Item2).ToArray();
            Type specificType = genericType.MakeGenericType(typeArgs);
            var i = Activator.CreateInstance(specificType, valueArgs);
            return i;
        }

        private Object RCreateEmptyList(Type t) {
            return Activator.CreateInstance(typeof(List<>).MakeGenericType(t));
        }
    }

    // Resource passed to systems to provide functionality to create/remove
    // entities, components and systems
    public interface IECS {
        public void AddEntity(List<Component> components);
    }
}