using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static class MonoAccessWeaver
{
    public static string Run(string file,string managed,string dependencies)
    {
        var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(managed);resolver.AddSearchDirectory(dependencies);
        using(var asm=AssemblyDefinition.ReadAssembly(file,new ReaderParameters{AssemblyResolver=resolver,InMemory=true})) {
            var module=asm.MainModule;
            var bridge=module.Types.Single(t=>t.Name=="ReleaseAccessBridge");
            var wrappers=new TypeDefinition("ResourceStoryMod","ReleaseAccessors",TypeAttributes.NotPublic|TypeAttributes.Abstract|TypeAttributes.Sealed,module.TypeSystem.Object);
            module.Types.Add(wrappers);
            var cache=new Dictionary<string,MethodDefinition>();
            var core=resolver.Resolve((AssemblyNameReference)module.TypeSystem.CoreLibrary);
            var typeFromHandle=module.ImportReference(core.MainModule.Types.Single(t=>t.FullName=="System.Type").Methods.Single(m=>m.Name=="GetTypeFromHandle"));
            var systemType=module.ImportReference(core.MainModule.Types.Single(t=>t.FullName=="System.Type"));
            var makeByRef=module.ImportReference(core.MainModule.Types.Single(t=>t.FullName=="System.Type").Methods.Single(m=>m.Name=="MakeByRefType"&&m.Parameters.Count==0));
            int fields=0,methods=0,sites=0;
            foreach(var type in AllTypes(module.Types).ToArray())foreach(var method in type.Methods.ToArray()) {
                if(!method.HasBody||type==wrappers||type==bridge)continue;
                foreach(var instruction in method.Body.Instructions.ToArray()) {
                    var field=instruction.Operand as FieldReference;
                    if(field!=null&&Target(field.DeclaringType)) {
                        var definition=field.Resolve();if(definition.IsPublic)continue;
                        string mode=instruction.OpCode.Code.ToString();
                        if(!new[]{"Ldfld","Ldsfld","Stfld","Stsfld","Ldflda","Ldsflda"}.Contains(mode))continue;
                        string key=mode+"|"+field.FullName;
                        if(!cache.TryGetValue(key,out var proxy)) {
                            bool write=mode.StartsWith("St"),address=mode.EndsWith("a"),isStatic=definition.IsStatic;
                            var owner=module.ImportReference(field.DeclaringType);var value=module.ImportReference(Bind(field.FieldType,field.DeclaringType));
                            proxy=new MethodDefinition("Field"+fields++,MethodAttributes.Assembly|MethodAttributes.Static,write?module.TypeSystem.Void:address?new ByReferenceType(value):value);
                            if(!isStatic)proxy.Parameters.Add(new ParameterDefinition(owner));
                            if(write)proxy.Parameters.Add(new ParameterDefinition(value));
                            wrappers.Methods.Add(proxy);cache[key]=proxy;var il=proxy.Body.GetILProcessor();
                            if(address&&!isStatic) {
                                il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldstr,field.Name);
                                var target=new GenericInstanceMethod(bridge.Methods.Single(m=>m.Name=="Ref"));target.GenericArguments.Add(owner);target.GenericArguments.Add(value);il.Emit(OpCodes.Call,target);
                            } else {
                                il.Emit(OpCodes.Ldtoken,owner);il.Emit(OpCodes.Call,typeFromHandle);il.Emit(OpCodes.Ldstr,field.Name);
                                if(!address){if(isStatic)il.Emit(OpCodes.Ldnull);else il.Emit(OpCodes.Ldarg_0);if(write)il.Emit(isStatic?OpCodes.Ldarg_0:OpCodes.Ldarg_1);}
                                var target=new GenericInstanceMethod(bridge.Methods.Single(m=>m.Name==(address?"StaticRef":write?"Set":"Get")));target.GenericArguments.Add(value);il.Emit(OpCodes.Call,target);
                            }
                            il.Emit(OpCodes.Ret);
                        }
                        instruction.OpCode=OpCodes.Call;instruction.Operand=proxy;sites++;
                    }
                    var called=instruction.Operand as MethodReference;
                    if(called==null||!Target(called.DeclaringType)||called is GenericInstanceMethod||instruction.OpCode.Code==Code.Newobj)continue;
                    var original=called.Resolve();if(original==null||original.IsPublic)continue;
                    if(instruction.OpCode.Code!=Code.Call&&instruction.OpCode.Code!=Code.Callvirt)continue;
                    string mkey="method|"+called.FullName;
                    if(!cache.TryGetValue(mkey,out var wrapper)) {
                        var owner=module.ImportReference(called.DeclaringType);var result=module.ImportReference(Bind(called.ReturnType,called.DeclaringType));
                        wrapper=new MethodDefinition("Method"+methods++,MethodAttributes.Assembly|MethodAttributes.Static,result);
                        bool instance=!original.IsStatic;
                        if(instance)wrapper.Parameters.Add(new ParameterDefinition(owner));
                        foreach(var p in called.Parameters)wrapper.Parameters.Add(new ParameterDefinition(module.ImportReference(Bind(p.ParameterType,called.DeclaringType))));
                        wrappers.Methods.Add(wrapper);cache[mkey]=wrapper;wrapper.Body.InitLocals=true;
                        var args=new VariableDefinition(new ArrayType(module.TypeSystem.Object));var returned=new VariableDefinition(module.TypeSystem.Object);wrapper.Body.Variables.Add(args);wrapper.Body.Variables.Add(returned);
                        var il=wrapper.Body.GetILProcessor();il.Emit(OpCodes.Ldc_I4,called.Parameters.Count);il.Emit(OpCodes.Newarr,module.TypeSystem.Object);il.Emit(OpCodes.Stloc,args);
                        for(int i=0;i<called.Parameters.Count;i++){
                            var p=wrapper.Parameters[i+(instance?1:0)];var value=p.ParameterType is ByReferenceType br?br.ElementType:p.ParameterType;
                            il.Emit(OpCodes.Ldloc,args);il.Emit(OpCodes.Ldc_I4,i);il.Emit(OpCodes.Ldarg,p);if(p.ParameterType.IsByReference)il.Emit(OpCodes.Ldobj,value);if(value.IsValueType||value.IsGenericParameter)il.Emit(OpCodes.Box,value);il.Emit(OpCodes.Stelem_Ref);
                        }
                        il.Emit(OpCodes.Ldtoken,owner);il.Emit(OpCodes.Call,typeFromHandle);il.Emit(OpCodes.Ldstr,called.Name);
                        il.Emit(OpCodes.Ldc_I4,called.Parameters.Count);il.Emit(OpCodes.Newarr,systemType);
                        for(int i=0;i<called.Parameters.Count;i++){
                            var parameter=wrapper.Parameters[i+(instance?1:0)].ParameterType;
                            il.Emit(OpCodes.Dup);il.Emit(OpCodes.Ldc_I4,i);il.Emit(OpCodes.Ldtoken,parameter is ByReferenceType byRef?byRef.ElementType:parameter);il.Emit(OpCodes.Call,typeFromHandle);
                            if(parameter.IsByReference)il.Emit(OpCodes.Callvirt,makeByRef);
                            il.Emit(OpCodes.Stelem_Ref);
                        }
                        if(instance)il.Emit(OpCodes.Ldarg_0);else il.Emit(OpCodes.Ldnull);il.Emit(OpCodes.Ldloc,args);il.Emit(OpCodes.Call,bridge.Methods.Single(m=>m.Name=="Invoke"));il.Emit(OpCodes.Stloc,returned);
                        for(int i=0;i<called.Parameters.Count;i++)if(wrapper.Parameters[i+(instance?1:0)].ParameterType is ByReferenceType br){il.Emit(OpCodes.Ldarg,wrapper.Parameters[i+(instance?1:0)]);il.Emit(OpCodes.Ldloc,args);il.Emit(OpCodes.Ldc_I4,i);il.Emit(OpCodes.Ldelem_Ref);il.Emit(OpCodes.Unbox_Any,br.ElementType);il.Emit(OpCodes.Stobj,br.ElementType);}
                        if(result.MetadataType!=MetadataType.Void){il.Emit(OpCodes.Ldloc,returned);il.Emit(result.IsValueType||result.IsGenericParameter?OpCodes.Unbox_Any:OpCodes.Castclass,result);}il.Emit(OpCodes.Ret);
                    }
                    instruction.OpCode=OpCodes.Call;instruction.Operand=wrapper;sites++;
                }
            }
            asm.Write(file);return "ACCESS_WEAVE_PASS fields="+fields+" methods="+methods+" sites="+sites;
        }
    }
    static bool Target(TypeReference type)=>type.Scope.Name=="Assembly-CSharp"||type.Scope.Name=="Assembly-CSharp.dll"||type.Scope.Name=="Unity.Localization"||type.Scope.Name=="Unity.Localization.dll";
    static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types){foreach(var t in types){yield return t;foreach(var n in AllTypes(t.NestedTypes))yield return n;}}
    static TypeReference Bind(TypeReference value,TypeReference owner){
        if(value is GenericParameter p&&p.Type==GenericParameterType.Type&&owner is GenericInstanceType g)return g.GenericArguments[p.Position];
        if(value is ByReferenceType b)return new ByReferenceType(Bind(b.ElementType,owner));
        if(value is ArrayType a)return new ArrayType(Bind(a.ElementType,owner),a.Rank);
        if(value is GenericInstanceType i){var r=new GenericInstanceType(i.ElementType);foreach(var v in i.GenericArguments)r.GenericArguments.Add(Bind(v,owner));return r;}return value;
    }
}
