// NOTE: The ray cast car demo is a copied and slightly modified version
//       of the vehicle example from the great JigLib. License follows.

/*
Copyright (c) 2007 Danny Chapman
http://www.rowlhouse.co.uk

This software is provided 'as-is', without any express or implied
warranty. In no event will the authors be held liable for any damages
arising from the use of this software.

Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:

1. The origin of this software must not be misrepresented; you must not
claim that you wrote the original software. If you use this software
in a product, an acknowledgment in the product documentation would be
appreciated but is not required.

2. Altered source versions must be plainly marked as such, and must not be
misrepresented as being the original software.

3. This notice may not be removed or altered from any source
distribution.
*/

using System;
using Jitter2;
using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;
using JitterDemo.Renderer;

namespace JitterDemo;

/// <summary>
/// A wheel which adds drive forces to a body.
/// Can be used to create a vehicle.
/// </summary>
public class Wheel
{
    private readonly World world;

    private readonly RigidBody car;

    private double displacement, upSpeed, lastDisplacement;
    private bool onFloor;
    private double driveTorque;

    private double angVel;

    /// used to estimate the friction
    private double angVelForGrip;

    private double torque;

    private readonly DynamicTree.RayCastFilterPre rayCast;

    /// <summary>
    /// Sets or gets the current steering angle of
    /// the wheel in degrees.
    /// </summary>
    public double SteerAngle { get; set; }

    /// <summary>
    /// Gets the current rotation of the wheel in degrees.
    /// </summary>
    public double WheelRotation { get; private set; }

    /// <summary>
    /// The damping factor of the supension spring.
    /// </summary>
    public double Damping { get; set; }

    /// <summary>
    /// The supension spring.
    /// </summary>
    public double Spring { get; set; }

    /// <summary>
    /// Inertia of the wheel.
    /// </summary>
    public double Inertia { get; set; }

    /// <summary>
    /// The wheel radius.
    /// </summary>
    public double Radius { get; set; }

    /// <summary>
    /// The friction of the car in the side direction.
    /// </summary>
    public double SideFriction { get; set; }

    /// <summary>
    /// Friction of the car in forward direction.
    /// </summary>
    public double ForwardFriction { get; set; }

    /// <summary>
    /// The length of the suspension spring.
    /// </summary>
    public double WheelTravel { get; set; }

    /// <summary>
    /// If set to true the wheel blocks.
    /// </summary>
    public bool Locked { get; set; }

    /// <summary>
    /// The highest possible velocity of the wheel.
    /// </summary>
    public double MaximumAngularVelocity { get; set; }

    /// <summary>
    /// The number of rays used for this wheel.
    /// </summary>
    public int NumberOfRays { get; set; }

    /// <summary>
    /// The position of the wheel in body space.
    /// </summary>
    public JVector Position { get; set; }

    public double AngularVelocity => angVel;

    public readonly JVector Up = JVector.UnitY;

    /// <summary>
    /// Creates a new instance of the Wheel class.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="car">The RigidBody on which to apply the wheel forces.</param>
    /// <param name="position">The position of the wheel on the body (in body space).</param>
    /// <param name="radius">The wheel radius.</param>
    public Wheel(World world, RigidBody car, JVector position, double radius)
    {
        this.world = world;
        this.car = car;
        Position = position;

        rayCast = RayCastCallback;

        // set some default values.
        SideFriction = 3.2;
        ForwardFriction = 5.0;
        Radius = radius;
        Inertia = 1.0;
        WheelTravel = 0.2;
        MaximumAngularVelocity = 200;
        NumberOfRays = 1;
    }

    /// <summary>
    /// Gets the position of the wheel in world space.
    /// </summary>
    /// <returns>The position of the wheel in world space.</returns>
    public JVector GetWheelCenter()
    {
        return Position + JVector.Transform(Up, car.Orientation) * displacement;
    }

    /// <summary>
    /// Adds drivetorque.
    /// </summary>
    /// <param name="torque">The amount of torque applied to this wheel.</param>
    public void AddTorque(double torque)
    {
        driveTorque += torque;
    }

    public void PostStep(double timeStep)
    {
        if (timeStep <= 0.0) return;

        double origAngVel = angVel;
        upSpeed = (displacement - lastDisplacement) / timeStep;

        if (Locked)
        {
            angVel = 0;
            torque = 0;
        }
        else
        {
            angVel += torque * timeStep / Inertia;
            torque = 0;

            if (!onFloor) driveTorque *= 0.1;

            // prevent friction from reversing dir - todo do this better
            // by limiting the torque
            if ((origAngVel > angVelForGrip && angVel < angVelForGrip) ||
                (origAngVel < angVelForGrip && angVel > angVelForGrip))
                angVel = angVelForGrip;

            angVel += driveTorque * timeStep / Inertia;
            driveTorque = 0;

            double maxAngVel = MaximumAngularVelocity;
            angVel = Math.Clamp(angVel, -maxAngVel, maxAngVel);

            WheelRotation += timeStep * angVel;
        }
    }

    public void PreStep(double timeStep)
    {
        // var dr = Playground.Instance.DebugRenderer;

        JVector force = JVector.Zero;
        lastDisplacement = displacement;
        displacement = 0.0;

        double vel = car.Velocity.Length();

        JVector worldPos = car.Position + JVector.Transform(Position, car.Orientation);
        JVector worldAxis = JVector.Transform(Up, car.Orientation);


        JVector forward = JVector.Transform(-JVector.UnitZ, car.Orientation); //-car.Orientation.GetColumn(2);
        JVector wheelFwd = JVector.Transform(forward, JMatrix.CreateRotationMatrix(worldAxis, SteerAngle));

        JVector wheelLeft = JVector.Cross(worldAxis, wheelFwd);
        wheelLeft.Normalize();

        JVector wheelUp = JVector.Cross(wheelFwd, wheelLeft);

        double rayLen = 2.0 * Radius + WheelTravel;

        JVector wheelRayEnd = worldPos - Radius * worldAxis;
        JVector wheelRayOrigin = wheelRayEnd + rayLen * worldAxis;
        JVector wheelRayDelta = wheelRayEnd - wheelRayOrigin;

        double deltaFwd = 2.0 * Radius / (NumberOfRays + 1);
        double deltaFwdStart = deltaFwd;

        onFloor = false;

        JVector groundNormal = JVector.Zero;
        JVector groundPos = JVector.Zero;
        double deepestFrac = double.MaxValue;
        RigidBody worldBody = null!;

        for (int i = 0; i < NumberOfRays; i++)
        {
            double distFwd = deltaFwdStart + i * deltaFwd - Radius;
            double zOffset = Radius * (1.0 - (double)Math.Cos(Math.PI / 2.0 * (distFwd / Radius)));

            JVector newOrigin = wheelRayOrigin + distFwd * wheelFwd + zOffset * wheelUp;

            RigidBody body;

            bool result = world.DynamicTree.RayCast(newOrigin, wheelRayDelta,
                rayCast, null, out IDynamicTreeProxy? shape, out JVector normal, out double frac);

            // Debug Rendering
            // dr.PushPoint(DebugRenderer.Color.Green, Conversion.FromJitter(newOrigin), 0.2);
            // dr.PushPoint(DebugRenderer.Color.Red, Conversion.FromJitter(newOrigin + wheelRayDelta), 0.2);

            JVector minBox = worldPos - new JVector(Radius);
            JVector maxBox = worldPos + new JVector(Radius);

            // dr.PushBox(DebugRenderer.Color.Green, Conversion.FromJitter(minBox), Conversion.FromJitter(maxBox));

            if (result && frac <= 1.0)
            {
                // shape must be RigidBodyShape since we filter out other ray tests
                body = (shape as RigidBodyShape)!.RigidBody;

                if (frac < deepestFrac)
                {
                    deepestFrac = frac;
                    groundPos = newOrigin + frac * wheelRayDelta;
                    worldBody = body;
                    groundNormal = normal;
                }

                onFloor = true;
            }
        }

        if (!onFloor) return;

        // dr.PushPoint(DebugRenderer.Color.Green, Conversion.FromJitter(groundPos), 0.2);

        if (groundNormal.LengthSquared() > 0.0) groundNormal.Normalize();

        // System.Diagnostics.Debug.WriteLine(groundPos.ToString());

        displacement = rayLen * (1.0 - deepestFrac);
        displacement = Math.Clamp(displacement, 0.0, WheelTravel);

        double displacementForceMag = displacement * Spring;

        // reduce force when suspension is par to ground
        displacementForceMag *= JVector.Dot(groundNormal, worldAxis);

        // apply damping
        double dampingForceMag = upSpeed * Damping;

        double totalForceMag = displacementForceMag + dampingForceMag;

        if (totalForceMag < 0.0) totalForceMag = 0.0;

        JVector extraForce = totalForceMag * worldAxis;

        force += extraForce;

        // side-slip friction and drive force. Work out wheel- and floor-relative coordinate frame
        JVector groundUp = groundNormal;
        JVector groundLeft = JVector.Cross(groundNormal, wheelFwd);
        if (groundLeft.LengthSquared() > 0.0) groundLeft.Normalize();

        JVector groundFwd = JVector.Cross(groundLeft, groundUp);

        JVector wheelPointVel = car.Velocity +
                                JVector.Cross(car.AngularVelocity, JVector.Transform(Position, car.Orientation));

        // rimVel=(wxr)*v
        JVector rimVel = angVel * JVector.Cross(wheelLeft, groundPos - worldPos);
        wheelPointVel += rimVel;

        if (worldBody == null) throw new Exception("car: world body is null.");

        JVector worldVel = worldBody.Velocity +
                           JVector.Cross(worldBody.AngularVelocity, groundPos - worldBody.Position);

        wheelPointVel -= worldVel;

        // sideways forces
        double noslipVel = 0.2;
        double slipVel = 0.4;
        double slipFactor = 0.7;

        double smallVel = 3.0;
        double friction = SideFriction;

        double sideVel = JVector.Dot(wheelPointVel, groundLeft);

        if (sideVel > slipVel || sideVel < -slipVel)
        {
            friction *= slipFactor;
        }
        else if (sideVel > noslipVel || sideVel < -noslipVel)
        {
            friction *= 1.0 - (1.0 - slipFactor) * (Math.Abs(sideVel) - noslipVel) / (slipVel - noslipVel);
        }

        if (sideVel < 0.0)
            friction *= -1.0;

        if (Math.Abs(sideVel) < smallVel)
            friction *= Math.Abs(sideVel) / smallVel;

        double sideForce = -friction * totalForceMag;

        extraForce = sideForce * groundLeft;
        force += extraForce;

        // fwd/back forces
        friction = ForwardFriction;
        double fwdVel = JVector.Dot(wheelPointVel, groundFwd);

        if (fwdVel > slipVel || fwdVel < -slipVel)
        {
            friction *= slipFactor;
        }
        else if (fwdVel > noslipVel || fwdVel < -noslipVel)
        {
            friction *= 1.0 - (1.0 - slipFactor) * (Math.Abs(fwdVel) - noslipVel) / (slipVel - noslipVel);
        }

        if (fwdVel < 0.0)
            friction *= -1.0;

        if (Math.Abs(fwdVel) < smallVel)
            friction *= Math.Abs(fwdVel) / smallVel;

        double fwdForce = -friction * totalForceMag;

        extraForce = fwdForce * groundFwd;
        force += extraForce;

        // fwd force also spins the wheel
        JVector wheelCentreVel = car.Velocity +
                                 JVector.Cross(car.AngularVelocity, JVector.Transform(Position, car.Orientation));

        angVelForGrip = JVector.Dot(wheelCentreVel, groundFwd) / Radius;
        torque += -fwdForce * Radius;

        // add force to car
        car.AddForce(force, groundPos);

        RenderWindow.Instance.DebugRenderer.PushPoint(DebugRenderer.Color.White, Conversion.FromJitter(groundPos), 0.2f);

        // add force to the world
        if (!worldBody.IsStatic)
        {
            const double maxOtherBodyAcc = 500.0;
            double maxOtherBodyForce = maxOtherBodyAcc * worldBody.Mass;

            if (force.LengthSquared() > (maxOtherBodyForce * maxOtherBodyForce))
                force *= maxOtherBodyForce / force.Length();

            worldBody.SetActivationState(true);

            worldBody.AddForce(force * -1, groundPos);
        }
    }

    private bool RayCastCallback(IDynamicTreeProxy shape)
    {
        if (shape is not RigidBodyShape rbs) return false;
        return rbs.RigidBody != car;
    }
}