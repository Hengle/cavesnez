// based on the FNA SpriteBatch implementation by Ethan Lee: https://github.com/FNA-XNA/FNA

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using Nez.BitmapFonts;


namespace Nez
{
	public class Batcher : GraphicsResource
	{
		public Matrix transformMatrix { get { return _transformMatrix; } }

		#region variables

		// Buffer objects used for actual drawing
		DynamicVertexBuffer _vertexBuffer;
		IndexBuffer _indexBuffer;

		// Local data stored before buffering to GPU
		VertexPositionColorTexture4[] _vertexInfo;
		Texture2D[] _textureInfo;

		// Default SpriteEffect
		Effect _spriteEffect;
		EffectParameter _spriteMatrixTransformParam;
		EffectPass _spriteEffectPass;

		// Tracks Begin/End calls
		bool _beginCalled;

		// Keep render state for non-Immediate modes.
		BlendState _blendState;
		SamplerState _samplerState;
		DepthStencilState _depthStencilState;
		RasterizerState _rasterizerState;
		bool _disableBatching;

		// How many sprites are in the current batch?
		int _numSprites;

		// Matrix to be used when creating the projection matrix
		Matrix _transformMatrix;

		// Matrix used internally to calculate the cameras projection
		Matrix _projectionMatrix;

		// this is the calculated MatrixTransform parameter in sprite shaders
		Matrix _matrixTransformMatrix;

		// User-provided Effect, if applicable
		Effect _customEffect;

		#endregion


		#region static variables and constants

		// As defined by the HiDef profile spec
		const int MAX_SPRITES = 2048;
		const int MAX_VERTICES = MAX_SPRITES * 4;
		const int MAX_INDICES = MAX_SPRITES * 6;

		// Used to calculate texture coordinates
		static readonly float[] _cornerOffsetX = new float[] { 0.0f, 1.0f, 0.0f, 1.0f };
		static readonly float[] _cornerOffsetY = new float[] { 0.0f, 0.0f, 1.0f, 1.0f };

		const string _spriteEffectName = "Microsoft.Xna.Framework.Graphics.Effect.Resources.SpriteEffect.ogl.mgfxo";
		static readonly short[] _indexData = generateIndexArray();
		static readonly byte[] _spriteEffectCode = EffectResource.getMonoGameEmbeddedResourceBytes( _spriteEffectName );

		#endregion


		public Batcher( GraphicsDevice graphicsDevice )
		{
			Assert.isTrue( graphicsDevice != null );
			
			this.graphicsDevice = graphicsDevice;

			_vertexInfo = new VertexPositionColorTexture4[MAX_SPRITES];
			_textureInfo = new Texture2D[MAX_SPRITES];
			_vertexBuffer = new DynamicVertexBuffer( graphicsDevice, typeof( VertexPositionColorTexture ), MAX_VERTICES, BufferUsage.WriteOnly );
			_indexBuffer = new IndexBuffer( graphicsDevice, IndexElementSize.SixteenBits, MAX_INDICES, BufferUsage.WriteOnly );
			_indexBuffer.SetData( _indexData );

			_spriteEffect = new Effect( graphicsDevice, _spriteEffectCode );
			_spriteMatrixTransformParam = _spriteEffect.Parameters["MatrixTransform"];
			_spriteEffectPass = _spriteEffect.CurrentTechnique.Passes[0];

			_projectionMatrix = new Matrix(
				0f, //(float)( 2.0 / (double)viewport.Width ) is the actual value we will use
				0.0f,
				0.0f,
				0.0f,
				0.0f,
				0f, //(float)( -2.0 / (double)viewport.Height ) is the actual value we will use
				0.0f,
				0.0f,
				0.0f,
				0.0f,
				1.0f,
				0.0f,
				-1.0f,
				1.0f,
				0.0f,
				1.0f
			);
		}


		protected override void Dispose( bool disposing )
		{
			if( !isDisposed && disposing )
			{
				_spriteEffect.Dispose();
				_indexBuffer.Dispose();
				_vertexBuffer.Dispose();
			}
			base.Dispose( disposing );
		}


		#region Public begin/end methods

		public void begin()
		{
			begin( BlendState.AlphaBlend, Core.defaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Matrix.Identity, false );
		}


		public void begin( Effect effect )
		{
			begin( BlendState.AlphaBlend, Core.defaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, effect, Matrix.Identity, false );
		}


		public void begin( Material material )
		{
			begin( material.blendState, material.samplerState, material.depthStencilState, RasterizerState.CullCounterClockwise, material.effect );
		}


		public void begin( Matrix transformationMatrix )
		{
			begin( BlendState.AlphaBlend, Core.defaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, transformationMatrix, false );
		}


		public void begin( BlendState blendState )
		{
			begin( blendState, Core.defaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Matrix.Identity, false );
		}


		public void begin( Material material, Matrix transformationMatrix )
		{
			begin( material.blendState, material.samplerState, material.depthStencilState, RasterizerState.CullCounterClockwise, material.effect, transformationMatrix, false );
		}


		public void begin( BlendState blendState, SamplerState samplerState, DepthStencilState depthStencilState, RasterizerState rasterizerState )
		{
			begin(
				blendState,
				samplerState,
				depthStencilState,
				rasterizerState,
				null,
				Matrix.Identity,
				false
			);
		}


		public void begin( BlendState blendState, SamplerState samplerState, DepthStencilState depthStencilState, RasterizerState rasterizerState, Effect effect )
		{
			begin(
				blendState,
				samplerState,
				depthStencilState,
				rasterizerState,
				effect,
				Matrix.Identity,
				false
			);
		}


		public void begin( BlendState blendState, SamplerState samplerState, DepthStencilState depthStencilState, RasterizerState rasterizerState,
			Effect effect, Matrix transformationMatrix )
		{
			begin(
				blendState,
				samplerState,
				depthStencilState,
				rasterizerState,
				effect,
				transformationMatrix,
				false
			);
		}


		public void begin( BlendState blendState, SamplerState samplerState, DepthStencilState depthStencilState, RasterizerState rasterizerState,
			Effect effect, Matrix transformationMatrix, bool disableBatching )
		{
			Assert.isFalse( _beginCalled, "Begin has been called before calling End after the last call to Begin. Begin cannot be called again until End has been successfully called." );
			_beginCalled = true;

			_blendState = blendState ?? BlendState.AlphaBlend;
			_samplerState = samplerState ?? Core.defaultSamplerState;
			_depthStencilState = depthStencilState ?? DepthStencilState.None;
			_rasterizerState = rasterizerState ?? RasterizerState.CullCounterClockwise;

			_customEffect = effect;
			_transformMatrix = transformationMatrix;
			_disableBatching = disableBatching;

			if( _disableBatching )
				prepRenderState();
		}


		public void end()
		{
			Assert.isTrue( _beginCalled, "End was called, but Begin has not yet been called. You must call Begin successfully before you can call End." );
			_beginCalled = false;

			if( !_disableBatching )
				flushBatch();

			_customEffect = null;
		}

		#endregion


		#region Public draw methods

		public void draw( Texture2D texture, Vector2 position )
		{
			checkBegin();
			pushSprite( texture, null, new Vector4( position.X, position.Y, 1.0f, 1.0f ),
				Color.White, Vector2.Zero, 0.0f, 0.0f, 0, false );
		}


		public void draw( Texture2D texture, Vector2 position, Color color )
		{
			checkBegin();
			pushSprite( texture, null, new Vector4( position.X, position.Y, 1.0f, 1.0f ),
				color, Vector2.Zero, 0.0f, 0.0f, 0, false );
		}


		public void draw( Texture2D texture, Rectangle destinationRectangle )
		{
			checkBegin();
			pushSprite( texture, null, new Vector4( destinationRectangle.X, destinationRectangle.Y, destinationRectangle.Width, destinationRectangle.Height ),
				Color.White, Vector2.Zero, 0.0f, 0.0f, 0, true );
		}


		public void draw( Texture2D texture, Rectangle destinationRectangle, Color color )
		{
			checkBegin();
			pushSprite( texture, null, new Vector4( destinationRectangle.X, destinationRectangle.Y, destinationRectangle.Width, destinationRectangle.Height ),
				color, Vector2.Zero, 0.0f, 0.0f, 0, true );
		}


		public void draw( Texture2D texture, Rectangle destinationRectangle, Rectangle? sourceRectangle, Color color )
		{
			checkBegin();
			pushSprite( texture, sourceRectangle, new Vector4( destinationRectangle.X, destinationRectangle.Y, destinationRectangle.Width, destinationRectangle.Height ),
				color, Vector2.Zero, 0.0f, 0.0f, 0, true );
		}


		public void draw( Texture2D texture, Vector2 position, Rectangle? sourceRectangle, Color color )
		{
			checkBegin();
			pushSprite(
				texture,
				sourceRectangle,
				new Vector4(
					position.X,
					position.Y,
					1.0f,
					1.0f
				),
				color,
				Vector2.Zero,
				0.0f,
				0.0f,
				0,
				false
			);
		}


		public void draw(
			Texture2D texture,
			Vector2 position,
			Rectangle? sourceRectangle,
			Color color,
			float rotation,
			Vector2 origin,
			float scale,
			SpriteEffects effects,
			float layerDepth
		)
		{
			checkBegin();
			pushSprite(
				texture,
				sourceRectangle,
				new Vector4(
					position.X,
					position.Y,
					scale,
					scale
				),
				color,
				origin,
				rotation,
				layerDepth,
				(byte)effects,
				false
			);
		}


		public void draw(
			Texture2D texture,
			Vector2 position,
			Rectangle? sourceRectangle,
			Color color,
			float rotation,
			Vector2 origin,
			Vector2 scale,
			SpriteEffects effects,
			float layerDepth
		)
		{
			checkBegin();
			pushSprite(
				texture,
				sourceRectangle,
				new Vector4(
					position.X,
					position.Y,
					scale.X,
					scale.Y
				),
				color,
				origin,
				rotation,
				layerDepth,
				(byte)effects,
				false
			);
		}


		public void draw(
			Texture2D texture,
			Rectangle destinationRectangle,
			Rectangle? sourceRectangle,
			Color color,
			float rotation,
			Vector2 origin,
			SpriteEffects effects,
			float layerDepth
		)
		{
			checkBegin();
			pushSprite(
				texture,
				sourceRectangle,
				new Vector4( destinationRectangle.X, destinationRectangle.Y, destinationRectangle.Width, destinationRectangle.Height ),
				color,
				origin,
				rotation,
				layerDepth,
				(byte)effects,
				true
			);
		}

		#endregion


		[System.Obsolete()]
		public void DrawString( SpriteFont spriteFont, string text, Vector2 position, Color color, float rotation,
			Vector2 origin, Vector2 scale, SpriteEffects effects, float layerDepth )
		{
			throw new NotImplementedException( "SpriteFont is too locked down to use directly. Wrap it in a NezSpriteFont" );
		}


		static short[] generateIndexArray()
		{
			short[] result = new short[MAX_INDICES];
			for( int i = 0, j = 0; i < MAX_INDICES; i += 6, j += 4 )
			{
				result[i] = (short)( j );
				result[i + 1] = (short)( j + 1 );
				result[i + 2] = (short)( j + 2 );
				result[i + 3] = (short)( j + 3 );
				result[i + 4] = (short)( j + 2 );
				result[i + 5] = (short)( j + 1 );
			}
			return result;
		}


		#region Methods

		void pushSprite( Texture2D texture, Rectangle? sourceRectangle, Vector4 destination, Color color, Vector2 origin,
			float rotation, float depth, byte effects, bool destSizeInPixels )
		{
			// Oh crap, we're out of space, flush!
			if( _numSprites >= MAX_SPRITES )
				flushBatch();

			// Source/Destination/Origin Calculations
			float sourceX, sourceY, sourceW, sourceH;
			var destW = destination.Z;
			var destH = destination.W;
			float originX, originY;
			if( sourceRectangle.HasValue )
			{
				float inverseTexW = 1.0f / (float)texture.Width;
				float inverseTexH = 1.0f / (float)texture.Height;

				sourceX = sourceRectangle.Value.X * inverseTexW;
				sourceY = sourceRectangle.Value.Y * inverseTexH;
				sourceW = Math.Max( sourceRectangle.Value.Width, float.Epsilon ) * inverseTexW;
				sourceH = Math.Max( sourceRectangle.Value.Height, float.Epsilon ) * inverseTexH;

				originX = ( origin.X / sourceW ) * inverseTexW;
				originY = ( origin.Y / sourceH ) * inverseTexH;

				if( !destSizeInPixels )
				{
					destW *= sourceRectangle.Value.Width;
					destH *= sourceRectangle.Value.Height;
				}
			}
			else
			{
				sourceX = 0.0f;
				sourceY = 0.0f;
				sourceW = 1.0f;
				sourceH = 1.0f;

				originX = origin.X * ( 1.0f / (float)texture.Width );
				originY = origin.Y * ( 1.0f / (float)texture.Height );

				if( !destSizeInPixels )
				{
					destW *= texture.Width;
					destH *= texture.Height;
				}
			}

			// Rotation Calculations
			float rotationMatrix1X;
			float rotationMatrix1Y;
			float rotationMatrix2X;
			float rotationMatrix2Y;
			if( !Mathf.withinEpsilon( rotation, 0.0f ) )
			{
				var sin = Mathf.sin( rotation );
				var cos = Mathf.cos( rotation );
				rotationMatrix1X = cos;
				rotationMatrix1Y = sin;
				rotationMatrix2X = -sin;
				rotationMatrix2Y = cos;
			}
			else
			{
				rotationMatrix1X = 1.0f;
				rotationMatrix1Y = 0.0f;
				rotationMatrix2X = 0.0f;
				rotationMatrix2Y = 1.0f;
			}

			// Calculate vertices, finally.
			var cornerX = ( _cornerOffsetX[0] - originX ) * destW;
			var cornerY = ( _cornerOffsetY[0] - originY ) * destH;
			_vertexInfo[_numSprites].position0.X = (
			    ( rotationMatrix2X * cornerY ) +
			    ( rotationMatrix1X * cornerX ) +
			    destination.X
			);
			_vertexInfo[_numSprites].position0.Y = (
			    ( rotationMatrix2Y * cornerY ) +
			    ( rotationMatrix1Y * cornerX ) +
			    destination.Y
			);
			cornerX = ( _cornerOffsetX[1] - originX ) * destW;
			cornerY = ( _cornerOffsetY[1] - originY ) * destH;
			_vertexInfo[_numSprites].position1.X = (
			    ( rotationMatrix2X * cornerY ) +
			    ( rotationMatrix1X * cornerX ) +
			    destination.X
			);
			_vertexInfo[_numSprites].position1.Y = (
			    ( rotationMatrix2Y * cornerY ) +
			    ( rotationMatrix1Y * cornerX ) +
			    destination.Y
			);
			cornerX = ( _cornerOffsetX[2] - originX ) * destW;
			cornerY = ( _cornerOffsetY[2] - originY ) * destH;
			_vertexInfo[_numSprites].position2.X = (
			    ( rotationMatrix2X * cornerY ) +
			    ( rotationMatrix1X * cornerX ) +
			    destination.X
			);
			_vertexInfo[_numSprites].position2.Y = (
			    ( rotationMatrix2Y * cornerY ) +
			    ( rotationMatrix1Y * cornerX ) +
			    destination.Y
			);
			cornerX = ( _cornerOffsetX[3] - originX ) * destW;
			cornerY = ( _cornerOffsetY[3] - originY ) * destH;
			_vertexInfo[_numSprites].position3.X = (
			    ( rotationMatrix2X * cornerY ) +
			    ( rotationMatrix1X * cornerX ) +
			    destination.X
			);
			_vertexInfo[_numSprites].position3.Y = (
			    ( rotationMatrix2Y * cornerY ) +
			    ( rotationMatrix1Y * cornerX ) +
			    destination.Y
			);
			_vertexInfo[_numSprites].textureCoordinate0.X = ( _cornerOffsetX[0 ^ effects] * sourceW ) + sourceX;
			_vertexInfo[_numSprites].textureCoordinate0.Y = ( _cornerOffsetY[0 ^ effects] * sourceH ) + sourceY;
			_vertexInfo[_numSprites].textureCoordinate1.X = ( _cornerOffsetX[1 ^ effects] * sourceW ) + sourceX;
			_vertexInfo[_numSprites].textureCoordinate1.Y = ( _cornerOffsetY[1 ^ effects] * sourceH ) + sourceY;
			_vertexInfo[_numSprites].textureCoordinate2.X = ( _cornerOffsetX[2 ^ effects] * sourceW ) + sourceX;
			_vertexInfo[_numSprites].textureCoordinate2.Y = ( _cornerOffsetY[2 ^ effects] * sourceH ) + sourceY;
			_vertexInfo[_numSprites].textureCoordinate3.X = ( _cornerOffsetX[3 ^ effects] * sourceW ) + sourceX;
			_vertexInfo[_numSprites].textureCoordinate3.Y = ( _cornerOffsetY[3 ^ effects] * sourceH ) + sourceY;
			_vertexInfo[_numSprites].position0.Z = depth;
			_vertexInfo[_numSprites].position1.Z = depth;
			_vertexInfo[_numSprites].position2.Z = depth;
			_vertexInfo[_numSprites].position3.Z = depth;
			_vertexInfo[_numSprites].color0 = color;
			_vertexInfo[_numSprites].color1 = color;
			_vertexInfo[_numSprites].color2 = color;
			_vertexInfo[_numSprites].color3 = color;

			if( _disableBatching )
			{
				_vertexBuffer.SetData( 0, _vertexInfo, 0, 1, VertexPositionColorTexture4.realStride, SetDataOptions.None );
				drawPrimitives( texture, 0, 1 );
			}
			else
			{
				_textureInfo[_numSprites] = texture;
				_numSprites += 1;
			}
		}


		void flushBatch()
		{
			if( _numSprites == 0 )
				return;

			int offset = 0;
			Texture2D curTexture = null;

			prepRenderState();

			_vertexBuffer.SetData( 0, _vertexInfo, 0, _numSprites, VertexPositionColorTexture4.realStride, SetDataOptions.None );

			curTexture = _textureInfo[0];
			for( var i = 0; i < _numSprites; i += 1 )
			{
				if( _textureInfo[i] != curTexture )
				{
					drawPrimitives( curTexture, offset, i - offset );
					curTexture = _textureInfo[i];
					offset = i;
				}
			}
			drawPrimitives( curTexture, offset, _numSprites - offset );

			_numSprites = 0;
		}


		void prepRenderState()
		{
			graphicsDevice.BlendState = _blendState;
			graphicsDevice.SamplerStates[0] = _samplerState;
			graphicsDevice.DepthStencilState = _depthStencilState;
			graphicsDevice.RasterizerState = _rasterizerState;

			graphicsDevice.SetVertexBuffer( _vertexBuffer );
			graphicsDevice.Indices = _indexBuffer;

			var viewport = graphicsDevice.Viewport;

			// inlined CreateOrthographicOffCenter
			_projectionMatrix.M11 = (float)( 2.0 / (double)viewport.Width );
			_projectionMatrix.M22 = (float)( -2.0 / (double)viewport.Height );


			_projectionMatrix.M41 = -1 - 0.5f * _projectionMatrix.M11;
			_projectionMatrix.M42 = 1 - 0.5f * _projectionMatrix.M22;

			Matrix.Multiply(
				ref _transformMatrix,
				ref _projectionMatrix,
				out _matrixTransformMatrix
			);
			_spriteMatrixTransformParam.SetValue( _matrixTransformMatrix );

			// we have to Apply here because custom effects often wont have a vertex shader and we need the default SpriteEffect's
			_spriteEffectPass.Apply();
		}


		void drawPrimitives( Texture texture, int baseSprite, int batchSize )
		{
			if( _customEffect != null )
			{
				foreach( var pass in _customEffect.CurrentTechnique.Passes )
				{
					pass.Apply();

					// Whatever happens in pass.Apply, make sure the texture being drawn ends up in Textures[0].
					graphicsDevice.Textures[0] = texture;
					graphicsDevice.DrawIndexedPrimitives( PrimitiveType.TriangleList, baseSprite * 4, 0, batchSize * 2 );
				}
			}
			else
			{
				graphicsDevice.Textures[0] = texture;
				graphicsDevice.DrawIndexedPrimitives( PrimitiveType.TriangleList, baseSprite * 4, 0, batchSize * 2 );
			}
		}


		[System.Diagnostics.Conditional( "DEBUG" )]
		void checkBegin()
		{
			if( !_beginCalled )
				throw new InvalidOperationException( "Begin has not been called. Begin must be called before you can draw" );
		}

		#endregion


		#region Sprite Data Container Class

		[StructLayout( LayoutKind.Sequential, Pack = 1 )]
		struct VertexPositionColorTexture4 : IVertexType
		{
			public const int realStride = 96;

			VertexDeclaration IVertexType.VertexDeclaration { get { throw new NotImplementedException(); } }

			public Vector3 position0;
			public Color color0;
			public Vector2 textureCoordinate0;
			public Vector3 position1;
			public Color color1;
			public Vector2 textureCoordinate1;
			public Vector3 position2;
			public Color color2;
			public Vector2 textureCoordinate2;
			public Vector3 position3;
			public Color color3;
			public Vector2 textureCoordinate3;
		}

		#endregion

	}
}
